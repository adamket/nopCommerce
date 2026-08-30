using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Transactions;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Humanizer;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

/// <summary>
/// Coordinates source paging and one transactional reconciliation unit per
/// Akeneo leaf. Destination-specific behavior remains in focused services.
/// </summary>
public class AkeneoProductBatchSyncService(
    IAkeneoApiClient akeneoApiClient,
    IAkeneoProductValueResolver productValueResolver,
    IAkeneoNopEntityMappingService entityMappingService,
    IAkeneoSyncItemLogService syncItemLogService,
    IProductService productService,
    IAkeneoFamilyMappingService familyMappingService,
    IAkeneoProductModelHierarchyResolver productModelHierarchyResolver,
    IAkeneoVariantRelationshipService variantRelationshipService,
    IAkeneoLeafRepresentationClassifier leafRepresentationClassifier,
    IAkeneoProductSyncService productSyncService,
    IAkeneoProductSyncStateService syncStateService,
    IAkeneoVariantRepresentationCleanupService representationCleanupService,
    IAkeneoCatalogReconciliationService catalogReconciliationService,
    IAkeneoSyncLeaseService syncLeaseService,
    IAkeneoProductModelDeltaFanOutService productModelDeltaFanOutService,
    IAkeneoDryRunChangeAnalyzer dryRunChangeAnalyzer,
    IAkeneoAssetMappingService assetMappingService,
    IAkeneoAssetResolver assetResolver)
    : IAkeneoProductBatchSyncService
{
    private static readonly JsonSerializerOptions SnapshotSerializerOptions = new()
    {
        WriteIndented = false
    };

    private const int ProductModelCodeBatchSize = 50;

    private static readonly TimeSpan LeaseRenewalInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromHours(6);

    public async Task<AkeneoProductBatchImportResult> SyncProductsAsync(
        AkeneoProductBatchImportRequest request,
        CancellationToken cancellationToken = default)
    {
        request ??= new AkeneoProductBatchImportRequest();
        ValidateRequest(request);

        var result = new AkeneoProductBatchImportResult
        {
            SyncRunRecordId = request.SyncRunRecordId
        };

        var pageSize = request.PageSize <= 0 ? 100 : request.PageSize;
        var parentProductCache = new Dictionary<string, Product>(StringComparer.OrdinalIgnoreCase);
        var productModelCache = new Dictionary<string, AkeneoProductDefinition>(StringComparer.OrdinalIgnoreCase);
        var processedProductKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lastLeaseRenewalUtc = DateTime.MinValue;
        var completedAllPhases = true;

        try
        {
            var useDeltaPhases = productModelDeltaFanOutService.ShouldRun(request);
            var directSearchJson = useDeltaPhases
                ? productModelDeltaFanOutService.BuildDirectProductSearchJson(request)
                : request.SearchJson;

            if (useDeltaPhases && !string.IsNullOrWhiteSpace(request.SearchAfter))
            {
                result.AddMessage(
                    "The supplied search-after cursor was ignored because delta fan-out " +
                    "uses separate phase-specific Akeneo searches.");
            }

            var directPhase = await ProcessProductSearchAsync(
                request,
                result,
                directSearchJson,
                useDeltaPhases ? null : request.SearchAfter,
                useDeltaPhases
                    ? _ => AkeneoSyncInclusionReason.DirectProductChange
                    : _ => AkeneoSyncInclusionReason.None,
                processedProductKeys,
                parentProductCache,
                productModelCache,
                pageSize,
                lastLeaseRenewalUtc,
                cancellationToken);

            lastLeaseRenewalUtc = directPhase.LastLeaseRenewalUtc;
            completedAllPhases &= directPhase.CompletedAllPages;

            if (directPhase.StopRun || result.WasTruncated)
                completedAllPhases = false;

            if (useDeltaPhases &&
                request.IncludeLinkedAssetUpdates &&
                !directPhase.StopRun &&
                !result.WasTruncated)
            {
                var assetPhase = await ProcessProductSearchAsync(
                    request,
                    result,
                    productModelDeltaFanOutService.BuildLinkedAssetProductSearchJson(request),
                    null,
                    _ => AkeneoSyncInclusionReason.LinkedAssetChange,
                    processedProductKeys,
                    parentProductCache,
                    productModelCache,
                    pageSize,
                    lastLeaseRenewalUtc,
                    cancellationToken);

                lastLeaseRenewalUtc = assetPhase.LastLeaseRenewalUtc;
                completedAllPhases &= assetPhase.CompletedAllPages;

                if (assetPhase.StopRun || result.WasTruncated)
                    completedAllPhases = false;
            }

            if (useDeltaPhases &&
                !result.WasTruncated &&
                (request.ContinueOnError || result.FailedCount == 0))
            {
                lastLeaseRenewalUtc = await RenewLeaseIfDueAsync(
                    request,
                    lastLeaseRenewalUtc);

                var fanOutPlan = await productModelDeltaFanOutService.BuildPlanAsync(
                    request,
                    cancellationToken);

                result.ChangedProductModelCount =
                    fanOutPlan.DirectChangedProductModelCount +
                    fanOutPlan.LinkedAssetOnlyProductModelCount;
                result.DescendantProductModelCount =
                    fanOutPlan.DescendantProductModelCount;
                result.ProductModelsReadCount = fanOutPlan.ProductModelsRead;

                if (fanOutPlan.HasAffectedProductModels)
                {
                    result.AddMessage(
                        $"Product-model delta fan-out discovered " +
                        $"{result.ChangedProductModelCount} changed product model(s), " +
                        $"{result.DescendantProductModelCount} descendant model(s), " +
                        $"{fanOutPlan.AffectedProductModelReasons.Count} affected model code(s), and " +
                        $"read {result.ProductModelsReadCount} product-model resource(s).");
                }

                foreach (var modelCodeBatch in fanOutPlan
                             .AffectedProductModelReasons
                             .Keys
                             .Chunk(ProductModelCodeBatchSize))
                {
                    if (result.WasTruncated ||
                        (!request.ContinueOnError && result.FailedCount > 0))
                    {
                        completedAllPhases = false;
                        break;
                    }

                    var fanOutPhase = await ProcessProductSearchAsync(
                        request,
                        result,
                        productModelDeltaFanOutService.BuildDescendantProductSearchJson(
                            request,
                            modelCodeBatch),
                        null,
                        source => ResolveFanOutReason(
                            source,
                            fanOutPlan.AffectedProductModelReasons),
                        processedProductKeys,
                        parentProductCache,
                        productModelCache,
                        pageSize,
                        lastLeaseRenewalUtc,
                        cancellationToken);

                    lastLeaseRenewalUtc = fanOutPhase.LastLeaseRenewalUtc;
                    completedAllPhases &= fanOutPhase.CompletedAllPages;

                    if (fanOutPhase.StopRun || result.WasTruncated)
                    {
                        completedAllPhases = false;
                        break;
                    }
                }
            }

            result.CompletedAllPages = completedAllPhases && !result.WasTruncated;

            if (request.IsAuthoritativeFullRun && result.IsAuthoritative)
            {
                result.ReconciledCount = await catalogReconciliationService
                    .ReconcileUnseenAsync(request, result, cancellationToken);
            }

            result.AddMessage(
                $"Product synchronization completed. Read: {result.TotalRead}, " +
                $"Created: {result.CreatedCount}, Updated: {result.UpdatedCount}, " +
                $"Skipped: {result.SkippedCount}, Failed: {result.FailedCount}, " +
                $"Reconciled: {result.ReconciledCount}. Delta inclusion: " +
                $"direct {result.DirectProductChangeCount}, " +
                $"ancestor {result.AncestorProductModelChangeCount}, " +
                $"linked asset {result.LinkedAssetChangeCount}; " +
                $"deduplicated {result.DuplicateCandidateCount} candidate(s).");

            return result;
        }
        catch (OperationCanceledException)
        {
            result.Canceled = true;
            result.CompletedAllPages = false;
            result.AddError("Product synchronization was canceled.");
            return result;
        }
        catch (Exception ex)
        {
            result.CompletedAllPages = false;
            result.AddError(ex.Message);
            return result;
        }
    }

    private async Task<ProductSearchPhaseResult> ProcessProductSearchAsync(
        AkeneoProductBatchImportRequest request,
        AkeneoProductBatchImportResult result,
        string searchJson,
        string? initialSearchAfter,
        Func<AkeneoProductDefinition, AkeneoSyncInclusionReason> inclusionReasonResolver,
        ISet<string> processedProductKeys,
        IDictionary<string, Product> parentProductCache,
        IDictionary<string, AkeneoProductDefinition> productModelCache,
        int pageSize,
        DateTime lastLeaseRenewalUtc,
        CancellationToken cancellationToken)
    {
        var searchAfter = initialSearchAfter;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lastLeaseRenewalUtc = await RenewLeaseIfDueAsync(
                request,
                lastLeaseRenewalUtc);

            if (HasReachedLimit(request, result))
            {
                MarkTruncated(request, result);
                return new ProductSearchPhaseResult(false, true, lastLeaseRenewalUtc);
            }

            var page = await akeneoApiClient.GetProductsPageAsync(
                GetEffectivePageSize(request, result, pageSize),
                searchAfter,
                searchJson,
                cancellationToken);

            if (page?.Items == null || page.Items.Count == 0)
                return new ProductSearchPhaseResult(true, false, lastLeaseRenewalUtc);

            foreach (var source in page.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                lastLeaseRenewalUtc = await RenewLeaseIfDueAsync(
                    request,
                    lastLeaseRenewalUtc);

                var sourceKey = GetSourceProductKey(source);
                if (!string.IsNullOrWhiteSpace(sourceKey) &&
                    !processedProductKeys.Add(sourceKey))
                {
                    result.DuplicateCandidateCount++;
                    continue;
                }

                if (HasReachedLimit(request, result))
                {
                    MarkTruncated(request, result);
                    return new ProductSearchPhaseResult(false, true, lastLeaseRenewalUtc);
                }

                result.TotalRead++;
                var inclusionReason = inclusionReasonResolver?.Invoke(source) ??
                    AkeneoSyncInclusionReason.None;
                RecordInclusionReason(result, inclusionReason);

                var itemResult = CreateInitialResult(source);
                itemResult.InclusionReason = inclusionReason;

                var snapshot = request.SaveRawPayloadSnapshot
                    ? SerializeSnapshot(source)
                    : null;

                try
                {
                    // Warm the product-model cache first so the reconcile
                    // unit performs no product-model HTTP inside its
                    // transaction. Asset binaries are downloaded by the
                    // reconcile unit's own pre-transaction prepare pass.
                    await PrefetchProductModelsAsync(
                        source,
                        productModelCache,
                        cancellationToken);

                    itemResult = await ReconcileProductUnitAsync(
                        source,
                        request,
                        itemResult,
                        parentProductCache,
                        productModelCache,
                        cancellationToken);
                    itemResult.InclusionReason = inclusionReason;

                    if (!itemResult.Success ||
                        itemResult.ActionType == SyncItemActionType.Failed)
                    {
                        ClearRolledBackDestination(itemResult);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    itemResult.AddError(ex.Message);
                    itemResult.ActionType = SyncItemActionType.Failed;
                    itemResult.InclusionReason = inclusionReason;
                    ClearRolledBackDestination(itemResult);
                }

                ApplyItemResultToBatchResult(result, itemResult);
                await SaveBatchItemLogIfNeededAsync(request, itemResult, snapshot);

                if (ShouldKeepItemResultInMemory(itemResult))
                    result.LoggedItemResults.Add(itemResult);

                if (!itemResult.Success && !request.ContinueOnError)
                {
                    result.AddError(
                        $"Synchronization stopped after product '{GetIdentity(itemResult)}' failed.");
                    return new ProductSearchPhaseResult(false, true, lastLeaseRenewalUtc);
                }
            }

            if (result.WasTruncated)
                return new ProductSearchPhaseResult(false, true, lastLeaseRenewalUtc);

            if (!page.HasNextPage)
                return new ProductSearchPhaseResult(true, false, lastLeaseRenewalUtc);

            searchAfter = page.SearchAfter;
        }
    }

    private static AkeneoSyncInclusionReason ResolveFanOutReason(
        AkeneoProductDefinition source,
        IDictionary<string, AkeneoSyncInclusionReason> productModelReasons)
    {
        var parentCode = source?.Parent?.Trim();
        return !string.IsNullOrWhiteSpace(parentCode) &&
               productModelReasons.TryGetValue(parentCode, out var reason)
            ? reason
            : AkeneoSyncInclusionReason.AncestorProductModelChange;
    }

    private static string GetSourceProductKey(AkeneoProductDefinition source)
    {
        var uuid = source?.Uuid?.Trim();
        if (!string.IsNullOrWhiteSpace(uuid))
            return "uuid:" + uuid;

        var identifier = source?.Identifier?.Trim();
        return string.IsNullOrWhiteSpace(identifier)
            ? null
            : "identifier:" + identifier;
    }

    private static void RecordInclusionReason(
        AkeneoProductBatchImportResult result,
        AkeneoSyncInclusionReason reason)
    {
        if (reason.HasFlag(AkeneoSyncInclusionReason.DirectProductChange))
            result.DirectProductChangeCount++;

        if (reason.HasFlag(AkeneoSyncInclusionReason.AncestorProductModelChange))
            result.AncestorProductModelChangeCount++;

        if (reason.HasFlag(AkeneoSyncInclusionReason.LinkedAssetChange))
            result.LinkedAssetChangeCount++;
    }

    private sealed record ProductSearchPhaseResult(
        bool CompletedAllPages,
        bool StopRun,
        DateTime LastLeaseRenewalUtc);

    public async Task<AkeneoProductImportResult> SyncProductByUuidAsync(
        AkeneoProductImportRequest request,
        CancellationToken cancellationToken = default)
    {
        request ??= new AkeneoProductImportRequest();
        ValidateRequest(request);

        var result = new AkeneoProductImportResult
        {
            AkeneoProductUuid = request.AkeneoProductUuid
        };

        string snapshot = null;

        try
        {
            if (string.IsNullOrWhiteSpace(request.AkeneoProductUuid))
            {
                result.AddError("Akeneo product UUID is required.");
                result.ActionType = SyncItemActionType.Failed;
                return await SaveLogAndReturnAsync(request, result, null);
            }

            var source = await akeneoApiClient.GetProductByUuidAsync(
                request.AkeneoProductUuid,
                cancellationToken);

            if (source == null)
            {
                result.AddError(
                    $"Akeneo product was not found. UUID: {request.AkeneoProductUuid}");
                result.ActionType = SyncItemActionType.Failed;
                return await SaveLogAndReturnAsync(request, result, null);
            }

            result = CreateInitialResult(source);
            snapshot = request.SaveRawPayloadSnapshot
                ? SerializeSnapshot(source)
                : null;

            var parentProductCache = new Dictionary<string, Product>(StringComparer.OrdinalIgnoreCase);
            var productModelCache = new Dictionary<string, AkeneoProductDefinition>(StringComparer.OrdinalIgnoreCase);

            // Warm the product-model cache first. The reconcile unit owns its
            // own transaction and downloads asset binaries in a pre-transaction
            // prepare pass, so no Akeneo HTTP happens while the DB transaction
            // is open.
            await PrefetchProductModelsAsync(
                source,
                productModelCache,
                cancellationToken);

            result = await ReconcileProductUnitAsync(
                source,
                request,
                result,
                parentProductCache,
                productModelCache,
                cancellationToken);

            if (!result.Success || result.ActionType == SyncItemActionType.Failed)
                ClearRolledBackDestination(result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            result.AddError(ex.Message);
            result.ActionType = SyncItemActionType.Failed;
            ClearRolledBackDestination(result);
        }

        return await SaveLogAndReturnAsync(request, result, snapshot);
    }

    public async Task<AkeneoProductImportResult> SyncProductModelByCodeAsync(
        AkeneoProductImportRequest request,
        CancellationToken cancellationToken = default)
    {
        request ??= new AkeneoProductImportRequest();
        ValidateRequest(request);

        var requestedCode = request.AkeneoProductModelCode?.Trim();
        var result = new AkeneoProductImportResult
        {
            AkeneoIdentifier = requestedCode,
            AkeneoProductKey = requestedCode
        };

        string snapshot = null;

        try
        {
            if (string.IsNullOrWhiteSpace(requestedCode))
            {
                result.AddError("Akeneo product model code is required.");
                result.ActionType = SyncItemActionType.Failed;
                return await SaveLogAndReturnAsync(request, result, null);
            }

            var source = await akeneoApiClient.GetProductModelByCodeAsync(
                requestedCode,
                cancellationToken);

            if (source == null)
            {
                result.AddError(
                    $"Akeneo product model was not found. Code: {requestedCode}");
                result.ActionType = SyncItemActionType.Failed;
                return await SaveLogAndReturnAsync(request, result, null);
            }

            var productModelCache = new Dictionary<string, AkeneoProductDefinition>(
                StringComparer.OrdinalIgnoreCase)
            {
                [requestedCode] = source
            };

            var effectiveSource = await ResolveEffectiveProductModelAsync(
                source,
                productModelCache,
                cancellationToken);

            result.AkeneoIdentifier = effectiveSource.Code;
            result.AkeneoProductKey = effectiveSource.Code;
            snapshot = request.SaveRawPayloadSnapshot
                ? SerializeSnapshot(effectiveSource)
                : null;

            if (!string.Equals(
                    requestedCode,
                    effectiveSource.Code,
                    StringComparison.OrdinalIgnoreCase))
            {
                result.AddMessage(
                    $"Product model '{requestedCode}' resolves to configured nopCommerce parent model '{effectiveSource.Code}'.");
            }

            var context = await productSyncService.PrepareAsync(
                effectiveSource, AkeneoEntityType.ProductModel, request, result, source.Family, cancellationToken);
            AppendParentNameResolutionDiagnostic(context, result, effectiveSource.Code, request);

            await productSyncService.RunPrepareAsync(context, cancellationToken);

            using (var transaction = CreateUnitTransaction())
            {
                await productSyncService.SynchronizeAsync(context, cancellationToken);
                if (result.Success && result.ActionType != SyncItemActionType.Failed)
                    transaction.Complete();
            }

            if (!result.Success || result.ActionType == SyncItemActionType.Failed)
                ClearRolledBackDestination(result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            result.AddError(ex.Message);
            result.ActionType = SyncItemActionType.Failed;
            ClearRolledBackDestination(result);
        }

        return await SaveLogAndReturnAsync(request, result, snapshot);
    }

    private async Task<AkeneoProductImportResult> ReconcileProductUnitAsync(
        AkeneoProductDefinition source,
        AkeneoProductImportRequest request,
        AkeneoProductImportResult result,
        IDictionary<string, Product> parentProductCache,
        IDictionary<string, AkeneoProductDefinition> productModelCache,
        CancellationToken cancellationToken)
    {
        var previousState = request.SyncProfileId.HasValue
            ? await syncStateService.GetBySourceAsync(
                request.SyncProfileId.Value,
                AkeneoEntityType.Product,
                source.Identifier,
                source.Uuid)
            : null;

        // Build a read-only fingerprint of the resolved nopCommerce desired
        // state before downloading asset binaries or opening a transaction.
        // Full runs intentionally never use this as a skip gate: they remain
        // authoritative destination reconciliations and can repair nopCommerce
        // drift even when Akeneo itself has not changed.
        var resolvedDesiredState = request.SyncProfileId.HasValue
            ? await TryResolveDesiredStateAsync(
                source,
                request,
                previousState,
                productModelCache,
                cancellationToken)
            : null;
        var desiredStateHash = resolvedDesiredState?.Hash;

        var skipLeafWrite = request.RunMode == AkeneoRunMode.Delta &&
                            previousState != null &&
                            previousState.LifecycleStatusId ==
                                (int)AkeneoProductLifecycleStatus.Active &&
                            !string.IsNullOrWhiteSpace(desiredStateHash) &&
                            string.Equals(
                                previousState.LastDesiredStateHash,
                                desiredStateHash,
                                StringComparison.OrdinalIgnoreCase);

        // One binary cache per item, shared by the parent and every leaf/child
        // context so the prepare pass and the write pass agree on cache hits.
        var assetBinaryCache = new Dictionary<string, AkeneoBinaryFile>(
            StringComparer.OrdinalIgnoreCase);

        // PASS 1 (no transaction): resolve mappings and download asset binaries
        // into the shared cache. This performs no nopCommerce writes, so the
        // network I/O happens while no DB transaction is open. It is
        // best-effort: any miss in the write pass falls back to an inline
        // download, so a prefetch failure only forfeits the optimization.
        try
        {
            await ImportProductAsync(
                source,
                request,
                CreateInitialResult(source),
                previousState,
                parentProductCache,
                productModelCache,
                prepareOnly: true,
                skipLeafWrite: skipLeafWrite,
                resolvedDesiredState: resolvedDesiredState,
                assetBinaryCache: assetBinaryCache,
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Prefetch is best-effort; the write pass handles real errors.
        }

        // PASS 2 (transactional): the atomic reconciliation unit. The write
        // pass reads binaries the prepare pass already downloaded.
        using var transaction = CreateUnitTransaction();

        result = await ImportProductAsync(
            source,
            request,
            result,
            previousState,
            parentProductCache,
            productModelCache,
            prepareOnly: false,
            skipLeafWrite: skipLeafWrite,
            resolvedDesiredState: resolvedDesiredState,
            assetBinaryCache: assetBinaryCache,
            cancellationToken: cancellationToken);

        if (!result.Success || result.ActionType == SyncItemActionType.Failed)
            return result; // scope left uncompleted -> rollback

        if (!skipLeafWrite)
        {
            await representationCleanupService.CleanupPreviousRepresentationAsync(
                previousState,
                result,
                cancellationToken);
        }

        if (request.SyncProfileId.HasValue)
        {
            await syncStateService.MarkSeenAsync(
                request.SyncProfileId.Value,
                request.SyncRunRecordId,
                AkeneoEntityType.Product,
                source.Identifier,
                source.Uuid,
                source.Parent,
                result,
                desiredStateHash);
        }

        transaction.Complete();
        return result;
    }

    private async Task<AkeneoProductImportResult> ImportProductAsync(
        AkeneoProductDefinition source,
        AkeneoProductImportRequest request,
        AkeneoProductImportResult result,
        AkeneoProductSyncState previousState,
        IDictionary<string, Product> parentProductCache,
        IDictionary<string, AkeneoProductDefinition> productModelCache,
        bool prepareOnly,
        bool skipLeafWrite,
        ResolvedDesiredState resolvedDesiredState,
        IDictionary<string, AkeneoBinaryFile> assetBinaryCache,
        CancellationToken cancellationToken)
    {
        var akeneoUuid = source.Uuid?.Trim();
        var akeneoIdentifier = source.Identifier?.Trim();

        if (string.IsNullOrWhiteSpace(akeneoUuid) &&
            string.IsNullOrWhiteSpace(akeneoIdentifier))
        {
            result.AddError("Akeneo product does not contain a UUID or identifier.");
            result.ActionType = SyncItemActionType.Failed;
            return result;
        }

        var immediateParentCode = source.Parent?.Trim();

        if (string.IsNullOrWhiteSpace(immediateParentCode))
        {
            if (skipLeafWrite)
            {
                if (!prepareOnly)
                {
                    ApplyPreviousStateBinding(result, previousState);
                    result.ActionType = SyncItemActionType.Skipped;
                    result.AddMessage(
                        "Skipped transactional product reconciliation because the resolved desired-state hash is unchanged. A full run will still verify destination state.");
                }

                return result;
            }

            var standaloneContext = resolvedDesiredState?.EffectiveLeafIntent != null
                ? await productSyncService.PrepareFromIntentAsync(
                    resolvedDesiredState.EffectiveLeafIntent,
                    result,
                    cancellationToken)
                : await productSyncService.PrepareAsync(
                    source,
                    AkeneoEntityType.Product,
                    request,
                    result,
                    cancellationToken);
            standaloneContext.PreloadedAssetBinaries = assetBinaryCache;

            if (prepareOnly)
            {
                await productSyncService.RunPrepareAsync(standaloneContext, cancellationToken);
                return result;
            }

            await productSyncService.SynchronizeAsync(
                standaloneContext,
                cancellationToken);

            result.DestinationKind = AkeneoProductDestinationKind.NopProduct;
            return result;
        }

        var ancestors = await GetProductModelAncestorsAsync(
            source,
            productModelCache,
            cancellationToken);

        if (ancestors.Count == 0)
        {
            result.AddError(
                $"Akeneo product model '{immediateParentCode}' was not found.");
            result.ActionType = SyncItemActionType.Failed;
            return result;
        }

        var familyCode = source.Family?.Trim();
        var familyVariantResolution = AkeneoLeafFamilyVariantResolver.Resolve(
            source,
            ancestors);

        if (!familyVariantResolution.Success)
        {
            result.AddError(familyVariantResolution.Error);
            result.ActionType = SyncItemActionType.Failed;
            return result;
        }

        var familyVariantCode = familyVariantResolution.FamilyVariantCode;

        var familyConfiguration = await familyMappingService
            .GetEffectiveMappingAsync(familyCode, familyVariantCode);

        var hierarchyMode = familyConfiguration is { Enabled: true }
            ? familyConfiguration.ProductModelHierarchyMode
            : AkeneoProductModelHierarchyMode.ImmediateParentProductModel;

        var hierarchy = productModelHierarchyResolver.Resolve(
            source,
            ancestors,
            hierarchyMode);

        // Prepare the unflattened leaf first so a standalone submodel rule can
        // still opt out of inherited values.
        var rawLeafContext = resolvedDesiredState?.RawLeafIntent != null
            ? await productSyncService.PrepareFromIntentAsync(
                resolvedDesiredState.RawLeafIntent,
                result,
                cancellationToken)
            : await productSyncService.PrepareAsync(
                source,
                AkeneoEntityType.Product,
                request,
                result,
                familyCode,
                familyVariantCode,
                AkeneoAttributeMappingScopeHelper.ResolveDefaultCurrentScope(
                    source,
                    AkeneoEntityType.Product),
                cancellationToken);
        rawLeafContext.PreloadedAssetBinaries = assetBinaryCache;

        if (!result.Success)
        {
            result.ActionType = SyncItemActionType.Failed;
            return result;
        }

        AkeneoVariantRelationshipMode? forcedMode = null;
        var overrideRule = await ResolveSubModelOverrideAsync(
            familyConfiguration,
            source,
            hierarchy.ImmediateParentModel);

        if (overrideRule != null)
        {
            if (overrideRule.VariantRelationshipOverrideMode ==
                AkeneoVariantRelationshipMode.None)
            {
                var existingMode = await leafRepresentationClassifier.ClassifyAsync(
                    rawLeafContext.ExistingProduct,
                    rawLeafContext.Sku);

                if (existingMode is null or AkeneoVariantRelationshipMode.None)
                {
                    var standaloneSource = overrideRule.MergeAncestorValues
                        ? hierarchy.LeafWithInheritedValues
                        : source;

                    // The Akeneo record still has a parent code, but this rule
                    // deliberately imports it as a standalone nopCommerce
                    // product. Pass the destination role explicitly so
                    // variant-only mappings are not applied to it.
                    var effectiveContext = resolvedDesiredState?.StandaloneIntent != null
                        ? await productSyncService.PrepareFromIntentAsync(
                            resolvedDesiredState.StandaloneIntent,
                            result,
                            cancellationToken)
                        : await productSyncService.PrepareAsync(
                            standaloneSource,
                            AkeneoEntityType.Product,
                            request,
                            result,
                            familyCode,
                            familyVariantCode,
                            AkeneoAttributeMappingEntityScope.StandaloneProduct,
                            cancellationToken);
                    effectiveContext.PreloadedAssetBinaries = assetBinaryCache;

                    if (!result.Success)
                    {
                        result.ActionType = SyncItemActionType.Failed;
                        return result;
                    }

                    if (skipLeafWrite)
                    {
                        if (!prepareOnly)
                        {
                            ApplyPreviousStateBinding(result, previousState);
                            result.Sku = effectiveContext.Sku;
                            result.ActionType = SyncItemActionType.Skipped;
                            result.AddMessage(
                                "Skipped transactional product reconciliation because the resolved desired-state hash is unchanged. A full run will still verify destination state.");
                        }

                        return result;
                    }

                    if (prepareOnly)
                    {
                        await productSyncService.RunPrepareAsync(effectiveContext, cancellationToken);
                        return result;
                    }

                    await productSyncService.SynchronizeAsync(
                        effectiveContext,
                        cancellationToken);

                    result.DestinationKind = AkeneoProductDestinationKind.NopProduct;
                    return result;
                }
            }
            else
            {
                forcedMode = overrideRule.VariantRelationshipOverrideMode;
            }
        }

        var effectiveSource = hierarchy.EffectiveLeaf;
        var leafContext = ReferenceEquals(effectiveSource, source)
            ? rawLeafContext
            : resolvedDesiredState?.EffectiveLeafIntent != null
                ? await productSyncService.PrepareFromIntentAsync(
                    resolvedDesiredState.EffectiveLeafIntent,
                    result,
                    cancellationToken)
                : await productSyncService.PrepareAsync(
                    effectiveSource,
                    AkeneoEntityType.Product,
                    request,
                    result,
                    familyCode,
                    familyVariantCode,
                    AkeneoAttributeMappingScopeHelper.ResolveDefaultCurrentScope(
                        effectiveSource,
                        AkeneoEntityType.Product),
                    cancellationToken);
        leafContext.PreloadedAssetBinaries = assetBinaryCache;

        if (!result.Success)
        {
            result.ActionType = SyncItemActionType.Failed;
            return result;
        }

        if (prepareOnly)
        {
            // Download the leaf's binaries, plus the parent product model's,
            // into the shared item cache. No nopCommerce writes happen here.
            if (!skipLeafWrite)
            {
                await productSyncService.RunPrepareAsync(
                    leafContext,
                    cancellationToken);
            }

            await PrepareParentAssetsAsync(
                hierarchy.EffectiveParentProductModel,
                familyCode,
                request,
                parentProductCache,
                assetBinaryCache,
                cancellationToken);
            return result;
        }

        var parentSync = await ResolveOrCreateParentProductAsync(
            hierarchy.EffectiveParentProductModel,
            familyCode,
            request,
            result,
            parentProductCache,
            assetBinaryCache,
            cancellationToken);

        var parentProduct = parentSync?.Product;

        if (parentProduct == null)
        {
            result.ActionType = SyncItemActionType.Failed;
            return result;
        }

        if (skipLeafWrite)
        {
            ApplyPreviousStateBinding(result, previousState);
            result.Sku = leafContext.Sku;
            result.ActionType = parentSync.Changed
                ? SyncItemActionType.Updated
                : SyncItemActionType.Skipped;
            result.AddMessage(
                parentSync.Changed
                    ? "Leaf reconciliation was skipped because its resolved desired-state hash is unchanged; the parent product model was reconciled and changed."
                    : "Skipped leaf reconciliation because the resolved desired-state hash is unchanged. The parent product model was still verified for this delta item.");
            return result;
        }

        var variantContext = await BuildVariantImportContextAsync(
            leafContext.Source,
            familyCode,
            familyConfiguration,
            request,
            leafContext,
            previousState,
            cancellationToken);

        variantContext.VariantRelationshipModeOverride = forcedMode;

        AkeneoVariantSyncResult variantResult;

        try
        {
            variantResult = await variantRelationshipService.ApplyAsync(
                parentProduct,
                variantContext,
                upsertChildProductAsync: () =>
                    productSyncService.SynchronizeAsync(
                        leafContext,
                        cancellationToken));
        }
        catch (NopException ex)
        {
            result.AddError(ex.Message);
            result.ActionType = SyncItemActionType.Failed;
            return result;
        }

        ApplyVariantResult(result, variantResult, previousState);

        // Parent product-model changes are part of this leaf reconciliation
        // unit. Promote an otherwise skipped leaf so the run counters and item
        // logs accurately show that the synchronization changed nopCommerce.
        if (parentSync.Changed &&
            result.ActionType == SyncItemActionType.Skipped)
        {
            result.ActionType = SyncItemActionType.Updated;
        }

        if (variantResult.Mode ==
            AkeneoVariantRelationshipMode.ProductAttributeCombinations)
        {
            // Legacy versions mapped a combination leaf UUID to the parent
            // product. Remove that ambiguous mapping; ProductSyncState now owns
            // the stable leaf-to-combination binding.
            if (!string.IsNullOrWhiteSpace(akeneoUuid))
            {
                await entityMappingService
                    .DeleteAkeneoNopEntityMappingByAkeneoUuidAsync(
                        AkeneoEntityType.Product,
                        akeneoUuid,
                        NopEntityType.Product);
            }

            if (!string.IsNullOrWhiteSpace(akeneoIdentifier))
            {
                await entityMappingService.DeleteAkeneoNopEntityMappingAsync(
                    AkeneoEntityType.Product,
                    akeneoIdentifier,
                    NopEntityType.Product);
            }
        }

        return result;
    }

    private static void ApplyVariantResult(
        AkeneoProductImportResult result,
        AkeneoVariantSyncResult variantResult,
        AkeneoProductSyncState previousState)
    {
        result.NopProductId = variantResult.NopProductId ?? 0;
        result.NopParentProductId = variantResult.NopParentProductId;
        result.NopProductAttributeCombinationId =
            variantResult.NopProductAttributeCombinationId;
        result.NopProductAttributeValueId =
            variantResult.NopProductAttributeValueId;

        result.DestinationKind = variantResult.Mode switch
        {
            AkeneoVariantRelationshipMode.GroupedProducts =>
                AkeneoProductDestinationKind.GroupedChildProduct,
            AkeneoVariantRelationshipMode.AssociatedToProductAttributeValue =>
                AkeneoProductDestinationKind.AssociatedProduct,
            AkeneoVariantRelationshipMode.ProductAttributeCombinations =>
                AkeneoProductDestinationKind.ProductAttributeCombination,
            _ => AkeneoProductDestinationKind.NopProduct
        };

        var bindingChanged = previousState == null ||
            previousState.DestinationKindId != (int)result.DestinationKind ||
            previousState.NopProductId != result.NopProductId ||
            previousState.NopProductAttributeCombinationId !=
                result.NopProductAttributeCombinationId ||
            previousState.NopProductAttributeValueId !=
                result.NopProductAttributeValueId;

        if (variantResult.DestinationCreated && previousState == null)
        {
            result.ActionType = SyncItemActionType.Created;
        }
        else if ((variantResult.Changed || bindingChanged) &&
                 result.ActionType == SyncItemActionType.Skipped)
        {
            result.ActionType = SyncItemActionType.Updated;
        }
    }

    private async Task<AkeneoVariantImportContext> BuildVariantImportContextAsync(
        AkeneoProductDefinition source,
        string familyCode,
        AkeneoFamilyMapping familyConfiguration,
        AkeneoProductImportRequest request,
        AkeneoProductSyncContext leafContext,
        AkeneoProductSyncState previousState,
        CancellationToken cancellationToken)
    {
        var context = new AkeneoVariantImportContext
        {
            AkeneoIdentifier = source.Identifier?.Trim(),
            AkeneoUuid = source.Uuid?.Trim(),
            AkeneoFamilyCode = familyCode,
            AkeneoFamilyVariantCode = leafContext.MappingFamilyVariantCode,
            Sku = leafContext.Sku,
            SyncProfileId = request.SyncProfileId,
            SyncRunRecordId = request.SyncRunRecordId,
            ExistingProductAttributeCombinationId =
                previousState?.NopProductAttributeCombinationId,
            ExistingAssociatedProductAttributeValueId =
                previousState?.NopProductAttributeValueId,
            SourceProduct = source,
            Locale = request.Locale,
            Channel = request.Channel,
            Currency = request.Currency
        };

        context.Price = TryResolveDecimalTarget(
            leafContext,
            "CombinationPrice",
            "Price");

        context.StockQuantity = TryResolveIntTarget(
            leafContext,
            "CombinationStockQuantity",
            "StockQuantity");

        if (familyConfiguration is not { Enabled: true })
            return context;

        var axisMappings = await familyMappingService
            .GetAxisMappingsAsync(familyConfiguration.Id);

        foreach (var axis in axisMappings)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!productValueResolver.TryGetValue(
                    source,
                    axis.AkeneoAttributeCode,
                    out var resolved,
                    request.Locale,
                    request.Channel,
                    request.Currency))
            {
                continue;
            }

            var axisValue = CreateAxisValue(
                axis.AkeneoAttributeCode,
                resolved);

            if (axisValue != null)
            {
                context.AxisValuesByAkeneoCode[axis.AkeneoAttributeCode] =
                    axisValue;
            }
        }

        return context;
    }

    private static AkeneoVariantAxisValue CreateAxisValue(
        string attributeCode,
        AkeneoResolvedProductValue resolved)
    {
        if (resolved == null)
            return null;

        string optionCode = null;

        if (resolved.RawData.HasValue)
        {
            var data = resolved.RawData.Value;
            optionCode = data.ValueKind switch
            {
                JsonValueKind.String => data.GetString(),
                JsonValueKind.Number => data.GetRawText(),
                JsonValueKind.Array => data.EnumerateArray()
                    .Select(item => item.ValueKind == JsonValueKind.String
                        ? item.GetString()
                        : item.GetRawText())
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
                _ => null
            };
        }

        var displayName = resolved.DisplayValues?.FirstOrDefault(value =>
                              !string.IsNullOrWhiteSpace(value))
                          ?? resolved.DisplayValue
                          ?? optionCode;

        if (string.IsNullOrWhiteSpace(displayName) &&
            string.IsNullOrWhiteSpace(optionCode))
        {
            return null;
        }

        return new AkeneoVariantAxisValue
        {
            AkeneoAttributeCode = attributeCode,
            AkeneoOptionCode = optionCode?.Trim(),
            DisplayName = displayName?.Trim()
        };
    }

    private static decimal? TryResolveDecimalTarget(
        AkeneoProductSyncContext context,
        params string[] targetKeys)
    {
        var mapped = context.MappedValues.FirstOrDefault(value =>
            value.HasValue &&
            value.TargetType == NopTargetType.ProductField &&
            targetKeys.Any(key => string.Equals(
                value.Mapping.NopTargetKey,
                key,
                StringComparison.OrdinalIgnoreCase)));

        return decimal.TryParse(
            mapped?.DisplayValue,
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;
    }

    private static int? TryResolveIntTarget(
        AkeneoProductSyncContext context,
        params string[] targetKeys)
    {
        var mapped = context.MappedValues.FirstOrDefault(value =>
            value.HasValue &&
            value.TargetType == NopTargetType.ProductField &&
            targetKeys.Any(key => string.Equals(
                value.Mapping.NopTargetKey,
                key,
                StringComparison.OrdinalIgnoreCase)));

        return int.TryParse(
            mapped?.DisplayValue,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;
    }

    private async Task<ParentProductSyncOutcome> ResolveOrCreateParentProductAsync(
        AkeneoProductDefinition effectiveProductModel,
        string mappingFamilyCode,
        AkeneoProductImportRequest request,
        AkeneoProductImportResult result,
        IDictionary<string, Product> parentProductCache,
        IDictionary<string, AkeneoBinaryFile> assetBinaryCache,
        CancellationToken cancellationToken)
    {
        var parentCode = effectiveProductModel?.Code?.Trim();
        if (string.IsNullOrWhiteSpace(parentCode))
        {
            result.AddError(
                "The selected Akeneo parent product model does not contain a code.");
            return null;
        }

        // This cache contains only parents synchronized during the current run.
        if (parentProductCache.TryGetValue(parentCode, out var cached))
        {
            var fresh = await productService.GetProductByIdAsync(cached.Id);

            if (fresh != null)
                return new ParentProductSyncOutcome(fresh, Changed: false);

            parentProductCache.Remove(parentCode);
        }

        var parentResult = new AkeneoProductImportResult
        {
            AkeneoIdentifier = parentCode,
            AkeneoProductKey = parentCode
        };

        // PrepareAsync resolves an existing nopCommerce parent by its product-
        // model entity mapping, while still reapplying current attribute maps.
        var parentContext = await productSyncService.PrepareAsync(
            effectiveProductModel,
            AkeneoEntityType.ProductModel,
            request,
            parentResult,
            mappingFamilyCode,
            cancellationToken);
        parentContext.PreloadedAssetBinaries = assetBinaryCache;

        AppendParentNameResolutionDiagnostic(
            parentContext,
            parentResult,
            parentCode,
            request);

        var parentProduct = await productSyncService.SynchronizeAsync(
            parentContext,
            cancellationToken);

        CopyMessages(
            parentResult,
            result,
            $"Product model '{parentCode}'");

        if (parentProduct == null)
            return null;

        parentProductCache[parentCode] = parentProduct;

        var changed = parentResult.ActionType is
            SyncItemActionType.Created or SyncItemActionType.Updated;

        return new ParentProductSyncOutcome(parentProduct, changed);
    }

    /// <summary>
    /// Downloads the parent product model's asset binaries into the shared item
    /// cache during the pre-transaction prepare pass. Read-only: it builds a
    /// throwaway context and runs only the prepare (download) pipeline, never a
    /// nopCommerce write. A parent already synchronized earlier in this run is
    /// skipped because its binaries were prepared when it was first seen.
    /// </summary>
    private async Task PrepareParentAssetsAsync(
        AkeneoProductDefinition effectiveProductModel,
        string mappingFamilyCode,
        AkeneoProductImportRequest request,
        IDictionary<string, Product> parentProductCache,
        IDictionary<string, AkeneoBinaryFile> assetBinaryCache,
        CancellationToken cancellationToken)
    {
        var parentCode = effectiveProductModel?.Code?.Trim();
        if (string.IsNullOrWhiteSpace(parentCode))
            return;

        // A parent already synchronized earlier in this run had its assets
        // prepared when its first child was processed; don't re-download them.
        if (parentProductCache.ContainsKey(parentCode))
            return;

        var parentResult = new AkeneoProductImportResult
        {
            AkeneoIdentifier = parentCode,
            AkeneoProductKey = parentCode
        };

        var parentContext = await productSyncService.PrepareAsync(
            effectiveProductModel,
            AkeneoEntityType.ProductModel,
            request,
            parentResult,
            mappingFamilyCode,
            cancellationToken);
        parentContext.PreloadedAssetBinaries = assetBinaryCache;

        await productSyncService.RunPrepareAsync(parentContext, cancellationToken);
    }

    private static void AppendParentNameResolutionDiagnostic(
        AkeneoProductSyncContext context,
        AkeneoProductImportResult result,
        string parentCode,
        AkeneoProductImportRequest request)
    {
        var nameMapping = context.MappedValues.FirstOrDefault(mapped =>
            mapped.TargetType == NopTargetType.ProductField &&
            string.Equals(
                mapped.Mapping.NopTargetKey,
                "Name",
                StringComparison.OrdinalIgnoreCase));

        if (nameMapping == null || nameMapping.HasValue)
            return;

        result.AddWarning(
            $"The parent Name mapping '{nameMapping.Mapping.AkeneoAttributeCode}' " +
            $"was selected from family scope '{context.MappingFamilyCode ?? "global"}', " +
            $"but no value resolved on product model '{parentCode}' for locale " +
            $"'{nameMapping.Mapping.Locale ?? request.Locale ?? "<none>"}' and channel " +
            $"'{nameMapping.Mapping.Channel ?? request.Channel ?? "<none>"}'. " +
            "The existing or fallback product-model name was preserved.");
    }

    private sealed record ParentProductSyncOutcome(
        Product Product,
        bool Changed);

    private static void CopyMessages(
        AkeneoProductImportResult source,
        AkeneoProductImportResult destination,
        string prefix)
    {
        foreach (var warning in source.Warnings)
            destination.AddWarning($"{prefix}: {warning}");

        foreach (var error in source.Errors)
            destination.AddError($"{prefix}: {error}");

        foreach (var message in source.Messages)
            destination.AddMessage($"{prefix}: {message}");
    }


    // Warms the entire product-model chain before opening the transaction so
    // hierarchy resolution performs no Akeneo HTTP requests inside the unit.
    private async Task PrefetchProductModelsAsync(
        AkeneoProductDefinition source,
        IDictionary<string, AkeneoProductDefinition> productModelCache,
        CancellationToken cancellationToken = default)
    {
        await GetProductModelAncestorsAsync(
            source,
            productModelCache,
            cancellationToken);
    }

    private async Task<AkeneoProductDefinition> ResolveEffectiveProductModelAsync(
        AkeneoProductDefinition productModel,
        IDictionary<string, AkeneoProductDefinition> productModelCache,
        CancellationToken cancellationToken = default)
    {
        var selectedCode = productModel?.Code?.Trim();
        if (string.IsNullOrWhiteSpace(selectedCode))
            return productModel;

        var ancestors = new List<AkeneoProductDefinition>();
        var visitedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            selectedCode
        };
        var currentCode = productModel.Parent?.Trim();

        while (!string.IsNullOrWhiteSpace(currentCode) &&
               visitedCodes.Add(currentCode))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var ancestor = await GetProductModelCachedAsync(
                currentCode,
                productModelCache,
                cancellationToken);

            if (ancestor == null)
                break;

            ancestors.Add(ancestor);
            currentCode = ancestor.Parent?.Trim();
        }

        var familyConfiguration = await familyMappingService
            .GetEffectiveMappingAsync(
                productModel.Family,
                productModel.FamilyVariant);

        var hierarchyMode = familyConfiguration is { Enabled: true }
            ? familyConfiguration.ProductModelHierarchyMode
            : AkeneoProductModelHierarchyMode.ImmediateParentProductModel;

        var syntheticLeaf = new AkeneoProductDefinition
        {
            Parent = selectedCode,
            Family = productModel.Family,
            FamilyVariant = productModel.FamilyVariant,
            Values = JsonSerializer.SerializeToElement(new Dictionary<string, object>())
        };

        var modelChain = new List<AkeneoProductDefinition> { productModel };
        modelChain.AddRange(ancestors);

        return productModelHierarchyResolver.Resolve(
                syntheticLeaf,
                modelChain,
                hierarchyMode)
            .EffectiveParentProductModel;
    }

    private async Task<IReadOnlyList<AkeneoProductDefinition>>
        GetProductModelAncestorsAsync(
            AkeneoProductDefinition source,
            IDictionary<string, AkeneoProductDefinition> productModelCache,
            CancellationToken cancellationToken = default)
    {
        var ancestors = new List<AkeneoProductDefinition>();
        var visitedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var currentCode = source?.Parent?.Trim();

        while (!string.IsNullOrWhiteSpace(currentCode) &&
               visitedCodes.Add(currentCode))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var model = await GetProductModelCachedAsync(
                currentCode,
                productModelCache,
                cancellationToken);

            if (model == null)
                break;

            ancestors.Add(model);
            currentCode = model.Parent?.Trim();
        }

        return ancestors;
    }

    private async Task<AkeneoProductDefinition> GetProductModelCachedAsync(
        string code,
        IDictionary<string, AkeneoProductDefinition> cache,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;

        if (cache.TryGetValue(code, out var cached))
            return cached;

        var model = await akeneoApiClient
            .GetProductModelByCodeAsync(code, cancellationToken);

        if (model != null)
            cache[code] = model;

        return model;
    }

    private async Task<AkeneoFamilySubModelRule> ResolveSubModelOverrideAsync(
        AkeneoFamilyMapping familyConfiguration,
        AkeneoProductDefinition leaf,
        AkeneoProductDefinition subModel)
    {
        if (familyConfiguration is not { Enabled: true })
            return null;

        var rules = await familyMappingService.GetSubModelRulesAsync(
            familyConfiguration.Id);

        return AkeneoSubModelRuleMatcher.FindMatch(rules, leaf, subModel);
    }

    private static AkeneoProductImportResult CreateInitialResult(
        AkeneoProductDefinition source) => new()
        {
            AkeneoProductUuid = source.Uuid,
            AkeneoIdentifier = source.Identifier,
            AkeneoProductKey = source.Uuid ?? source.Identifier,
            ActionType = SyncItemActionType.Skipped
        };

    private static void ValidateRequest(AkeneoProductImportRequest request)
    {
        if (request.SyncRunRecordId <= 0)
        {
            throw new ArgumentException(
                "SyncRunRecordId must be provided in the request.",
                nameof(request));
        }
    }

    private static bool HasReachedLimit(
        AkeneoProductBatchImportRequest request,
        AkeneoProductBatchImportResult result) =>
        request.MaxProducts.HasValue &&
        result.TotalRead >= request.MaxProducts.Value;

    private static int GetEffectivePageSize(
        AkeneoProductBatchImportRequest request,
        AkeneoProductBatchImportResult result,
        int pageSize)
    {
        if (!request.MaxProducts.HasValue)
            return pageSize;

        return Math.Max(
            1,
            Math.Min(pageSize, request.MaxProducts.Value - result.TotalRead));
    }

    private static void MarkTruncated(
        AkeneoProductBatchImportRequest request,
        AkeneoProductBatchImportResult result)
    {
        result.WasTruncated = true;
        result.CompletedAllPages = false;
        result.AddMessage(
            $"Stopped after reaching MaxProducts limit of {request.MaxProducts}.");
    }

    private static void ApplyItemResultToBatchResult(
        AkeneoProductBatchImportResult batchResult,
        AkeneoProductImportResult itemResult)
    {
        if (itemResult.Warnings.Any())
            batchResult.WarningCount++;

        if (!itemResult.Success ||
            itemResult.ActionType == SyncItemActionType.Failed)
        {
            batchResult.FailedCount++;

            foreach (var error in itemResult.Errors)
            {
                if (batchResult.Errors.Count >= 100)
                    break;

                batchResult.AddError($"{GetIdentity(itemResult)}: {error}");
            }

            return;
        }

        switch (itemResult.ActionType)
        {
            case SyncItemActionType.Created:
                batchResult.CreatedCount++;
                break;
            case SyncItemActionType.Updated:
                batchResult.UpdatedCount++;
                break;
            default:
                batchResult.SkippedCount++;

                if (batchResult.SkippedSkuSample.Count < 50)
                {
                    var identity = GetIdentity(itemResult);
                    if (!string.IsNullOrWhiteSpace(identity))
                        batchResult.SkippedSkuSample.Add(identity);
                }
                break;
        }
    }

    private async Task SaveBatchItemLogIfNeededAsync(
        AkeneoProductImportRequest request,
        AkeneoProductImportResult result,
        string rawPayloadSnapshot)
    {
        var shouldWrite =
            result.ActionType != SyncItemActionType.Skipped ||
            result.Errors.Any() ||
            result.Warnings.Any() ||
            HasDeltaInclusionReason(result.InclusionReason);

        if (!shouldWrite)
            return;

        await syncItemLogService.InsertAkeneoSyncItemLogAsync(
            new AkeneoSyncItemLog
            {
                SyncRunRecordId = request.SyncRunRecordId,
                AkeneoProductUuid = result.AkeneoProductUuid ?? request.AkeneoProductUuid,
                AkeneoIdentifier = result.AkeneoIdentifier ?? result.Sku,
                NopProductId = result.NopProductId,
                ActionTypeId = (int)result.ActionType,
                Message = BuildLogMessage(result),
                RawPayloadSnapshot = rawPayloadSnapshot,
                CreatedOnUtc = DateTime.UtcNow
            });
    }

    private async Task<AkeneoProductImportResult> SaveLogAndReturnAsync(
        AkeneoProductImportRequest request,
        AkeneoProductImportResult result,
        string rawPayloadSnapshot)
    {
        await SaveBatchItemLogIfNeededAsync(request, result, rawPayloadSnapshot);
        return result;
    }

    private static string BuildLogMessage(AkeneoProductImportResult result)
    {
        var parts = new List<string>();

        if (result.InclusionReason != AkeneoSyncInclusionReason.None)
            parts.Add("Included because: " + FormatInclusionReason(result.InclusionReason));

        if (result.Messages.Any())
            parts.Add("Messages: " + string.Join(" | ", result.Messages));

        if (result.Warnings.Any())
            parts.Add("Warnings: " + string.Join(" | ", result.Warnings));

        if (result.Errors.Any())
            parts.Add("Errors: " + string.Join(" | ", result.Errors));

        return string.Join(Environment.NewLine, parts).Truncate(4000);
    }

    private static bool ShouldKeepItemResultInMemory(
        AkeneoProductImportResult result) =>
        result.ActionType != SyncItemActionType.Skipped ||
        result.Errors.Any() ||
        result.Warnings.Any() ||
        HasDeltaInclusionReason(result.InclusionReason);

    private static bool HasDeltaInclusionReason(
        AkeneoSyncInclusionReason reason) =>
        reason != AkeneoSyncInclusionReason.None;

    private static string FormatInclusionReason(
        AkeneoSyncInclusionReason reason)
    {
        var reasons = new List<string>();

        if (reason.HasFlag(AkeneoSyncInclusionReason.DirectProductChange))
            reasons.Add("direct product change");

        if (reason.HasFlag(AkeneoSyncInclusionReason.AncestorProductModelChange))
            reasons.Add("ancestor product-model change");

        if (reason.HasFlag(AkeneoSyncInclusionReason.LinkedAssetChange))
            reasons.Add("linked asset change");

        return reasons.Count == 0
            ? "profile scope"
            : string.Join(", ", reasons);
    }

    private static void ClearRolledBackDestination(
        AkeneoProductImportResult result)
    {
        result.NopProductId = 0;
        result.NopParentProductId = null;
        result.NopProductAttributeCombinationId = null;
        result.NopProductAttributeValueId = null;
    }

    private sealed record ResolvedDesiredState(
        string Hash,
        AkeneoResolvedProductIntent RawLeafIntent,
        AkeneoResolvedProductIntent EffectiveLeafIntent,
        AkeneoResolvedProductIntent StandaloneIntent);

    private static string GetIdentity(AkeneoProductImportResult result) =>
        result.Sku ??
        result.AkeneoIdentifier ??
        result.AkeneoProductUuid ??
        "(unknown product)";

    private static string SerializeSnapshot(AkeneoProductDefinition source) =>
        JsonSerializer.Serialize(source, SnapshotSerializerOptions).Truncate(12000);

    private async Task<ResolvedDesiredState> TryResolveDesiredStateAsync(
        AkeneoProductDefinition source,
        AkeneoProductImportRequest request,
        AkeneoProductSyncState previousState,
        IDictionary<string, AkeneoProductDefinition> productModelCache,
        CancellationToken cancellationToken)
    {
        try
        {
            var hashResult = CreateInitialResult(source);
            var immediateParentCode = source.Parent?.Trim();

            if (string.IsNullOrWhiteSpace(immediateParentCode))
            {
                var intent = await productSyncService.ResolveIntentAsync(
                    source,
                    AkeneoEntityType.Product,
                    request,
                    cancellationToken);
                var context = await productSyncService.PrepareFromIntentAsync(
                    intent,
                    hashResult,
                    cancellationToken);

                var hash = await BuildResolvedContextHashAsync(
                    context,
                    request,
                    new
                    {
                        Path = "standalone",
                        ParentCode = (string)null
                    },
                    cancellationToken);

                return string.IsNullOrWhiteSpace(hash)
                    ? null
                    : new ResolvedDesiredState(
                        hash,
                        RawLeafIntent: null,
                        EffectiveLeafIntent: intent,
                        StandaloneIntent: null);
            }

            var ancestors = await GetProductModelAncestorsAsync(
                source,
                productModelCache,
                cancellationToken);

            if (ancestors.Count == 0)
                return null;

            var familyCode = source.Family?.Trim();
            var familyVariantResolution = AkeneoLeafFamilyVariantResolver.Resolve(
                source,
                ancestors);

            if (!familyVariantResolution.Success)
            {
                hashResult.AddError(familyVariantResolution.Error);
                return null;
            }

            var familyVariantCode = familyVariantResolution.FamilyVariantCode;

            var familyConfiguration = await familyMappingService
                .GetEffectiveMappingAsync(
                    familyCode,
                    familyVariantCode);

            var hierarchyMode = familyConfiguration is { Enabled: true }
                ? familyConfiguration.ProductModelHierarchyMode
                : AkeneoProductModelHierarchyMode.ImmediateParentProductModel;
            var hierarchy = productModelHierarchyResolver.Resolve(
                source,
                ancestors,
                hierarchyMode);

            var rawLeafIntent = await productSyncService.ResolveIntentAsync(
                source,
                AkeneoEntityType.Product,
                request,
                familyCode,
                familyVariantCode,
                AkeneoAttributeMappingScopeHelper.ResolveDefaultCurrentScope(
                    source,
                    AkeneoEntityType.Product),
                cancellationToken);
            var rawLeafContext = await productSyncService.PrepareFromIntentAsync(
                rawLeafIntent,
                hashResult,
                cancellationToken);

            if (!hashResult.Success)
                return null;

            AkeneoVariantRelationshipMode? forcedMode = null;
            var overrideRule = await ResolveSubModelOverrideAsync(
                familyConfiguration,
                source,
                hierarchy.ImmediateParentModel);

            if (overrideRule != null)
            {
                if (overrideRule.VariantRelationshipOverrideMode ==
                    AkeneoVariantRelationshipMode.None)
                {
                    var existingMode = await leafRepresentationClassifier.ClassifyAsync(
                        rawLeafContext.ExistingProduct,
                        rawLeafContext.Sku);

                    if (existingMode is null or AkeneoVariantRelationshipMode.None)
                    {
                        var standaloneSource = overrideRule.MergeAncestorValues
                            ? hierarchy.LeafWithInheritedValues
                            : source;
                        var standaloneIntent = await productSyncService.ResolveIntentAsync(
                            standaloneSource,
                            AkeneoEntityType.Product,
                            request,
                            familyCode,
                            familyVariantCode,
                            AkeneoAttributeMappingEntityScope.StandaloneProduct,
                            cancellationToken);
                        var context = await productSyncService.PrepareFromIntentAsync(
                            standaloneIntent,
                            hashResult,
                            cancellationToken);

                        var hash = await BuildResolvedContextHashAsync(
                            context,
                            request,
                            new
                            {
                                Path = "submodel-standalone",
                                ParentCode = immediateParentCode,
                                MergeAncestorValues = overrideRule.MergeAncestorValues,
                                OverrideRule = BuildSubModelRuleFingerprint(overrideRule)
                            },
                            cancellationToken);

                        return string.IsNullOrWhiteSpace(hash)
                            ? null
                            : new ResolvedDesiredState(
                                hash,
                                RawLeafIntent: rawLeafIntent,
                                EffectiveLeafIntent: null,
                                StandaloneIntent: standaloneIntent);
                    }
                }
                else
                {
                    forcedMode = overrideRule.VariantRelationshipOverrideMode;
                }
            }

            var effectiveSource = hierarchy.EffectiveLeaf;
            var leafIntent = ReferenceEquals(effectiveSource, source)
                ? rawLeafIntent
                : await productSyncService.ResolveIntentAsync(
                    effectiveSource,
                    AkeneoEntityType.Product,
                    request,
                    familyCode,
                    familyVariantCode,
                    AkeneoAttributeMappingScopeHelper.ResolveDefaultCurrentScope(
                        effectiveSource,
                        AkeneoEntityType.Product),
                    cancellationToken);
            var leafContext = ReferenceEquals(leafIntent, rawLeafIntent)
                ? rawLeafContext
                : await productSyncService.PrepareFromIntentAsync(
                    leafIntent,
                    hashResult,
                    cancellationToken);

            if (!hashResult.Success)
                return null;

            var variantContext = await BuildVariantImportContextAsync(
                leafIntent.Source,
                familyCode,
                familyConfiguration,
                request,
                leafContext,
                previousState,
                cancellationToken);

            IReadOnlyList<AkeneoFamilyVariantAxisMapping> axisMappings =
                familyConfiguration is { Enabled: true }
                    ? (await familyMappingService.GetAxisMappingsAsync(
                        familyConfiguration.Id)).ToList()
                    : Array.Empty<AkeneoFamilyVariantAxisMapping>();

            var desiredStateHash = await BuildResolvedContextHashAsync(
                leafContext,
                request,
                new
                {
                    Path = "variant",
                    ImmediateParentCode = hierarchy.ImmediateParentModel?.Code?.Trim(),
                    EffectiveParentCode = hierarchy.EffectiveParentProductModel?.Code?.Trim(),
                    HierarchyMode = (int)hierarchy.Mode,
                    IsFlattened = hierarchy.IsFlattened,
                    ForcedMode = forcedMode.HasValue ? (int?)forcedMode.Value : null,
                    Family = BuildFamilyMappingFingerprint(familyConfiguration),
                    OverrideRule = BuildSubModelRuleFingerprint(overrideRule),
                    AxisMappings = axisMappings
                        .OrderBy(axis => axis.DisplayOrder)
                        .ThenBy(axis => axis.Id)
                        .Select(axis => new
                        {
                            axis.AkeneoAttributeCode,
                            axis.NopProductAttributeId,
                            axis.IsRequired,
                            axis.DisplayOrder,
                            axis.AkeneoVariantAxisLevel
                        })
                        .ToList(),
                    AxisValues = variantContext.AxisValuesByAkeneoCode
                        .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(pair => new
                        {
                            AttributeCode = pair.Key,
                            pair.Value.AkeneoOptionCode,
                            pair.Value.DisplayName
                        })
                        .ToList(),
                    variantContext.Price,
                    variantContext.StockQuantity
                },
                cancellationToken);

            return string.IsNullOrWhiteSpace(desiredStateHash)
                ? null
                : new ResolvedDesiredState(
                    desiredStateHash,
                    RawLeafIntent: rawLeafIntent,
                    EffectiveLeafIntent: leafIntent,
                    StandaloneIntent: null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Desired-state analysis is an optimization only. Any uncertainty
            // falls back to the normal transactional reconciliation path.
            return null;
        }
    }

    internal async Task<string> BuildResolvedContextHashAsync(
        AkeneoProductSyncContext context,
        AkeneoProductImportRequest request,
        object representation,
        CancellationToken cancellationToken)
    {
        if (context == null || !context.Result.Success)
            return null;

        var preview = new AkeneoProductMappingPreviewModel
        {
            AkeneoProductUuid = context.SourceUuid,
            AkeneoIdentifier = context.SourceCode,
            AkeneoEntityTypeId = (int)context.SourceEntityType,
            SyncProfileId = request.SyncProfileId,
            Locale = request.Locale,
            Channel = request.Channel,
            Currency = request.Currency,
            NopProductFound = context.ExistingProduct != null,
            NopProductId = context.ExistingProduct?.Id,
            AkeneoProductFound = true,
            HasSearched = true
        };

        await dryRunChangeAnalyzer.AnalyzeAsync(
            context,
            preview,
            cancellationToken);

        if (preview.Errors.Any() || context.Result.Errors.Any())
            return null;

        var assets = await BuildResolvedAssetFingerprintAsync(
            context,
            cancellationToken);

        if (assets == null)
            return null;

        var categories = await BuildCategoryDestinationFingerprintAsync(
            context,
            cancellationToken);

        var payload = new
        {
            Version = 3,
            Request = BuildWritePolicyFingerprint(request),
            Representation = representation,
            Context = new
            {
                context.SourceCode,
                context.SourceUuid,
                context.MappingFamilyCode,
                context.MappingFamilyVariantCode,
                MappingEntityScope = (int)context.MappingEntityScope,
                context.Sku,
                SourceEnabled = context.Source.Enabled,
                SourceParent = context.Source.Parent?.Trim()
            },
            MappedValues = context.MappedValues
                .OrderBy(value => value.Mapping?.Id ?? 0)
                .ThenBy(value => value.Mapping?.MappingKey, StringComparer.OrdinalIgnoreCase)
                .ThenBy(value => value.Mapping?.AkeneoAttributeCode, StringComparer.OrdinalIgnoreCase)
                .Select(value => new
                {
                    Mapping = value.Mapping == null
                        ? null
                        : new
                        {
                            value.Mapping.MappingKey,
                            value.Mapping.Name,
                            value.Mapping.ValueModeId,
                            value.Mapping.ValueTemplate,
                            value.Mapping.AkeneoFamilyCode,
                            value.Mapping.AkeneoAttributeCode,
                            value.Mapping.AkeneoAttributeTypeId,
                            value.Mapping.AkeneoReferenceEntityCode,
                            value.Mapping.AkeneoReferenceEntityAttributeCode,
                            value.Mapping.NopTargetTypeId,
                            value.Mapping.NopTargetEntityId,
                            value.Mapping.NopTargetKey,
                            value.Mapping.SpecificationMissingValueHandlingId,
                            value.Mapping.Locale,
                            value.Mapping.Channel,
                            value.Mapping.TransformRuleJson,
                            value.Mapping.IsRequired,
                            value.Mapping.EntityScopeId
                        },
                    value.HasValue,
                    value.ResolvedSourceDisplayName,
                    Value = value.Value == null
                        ? null
                        : new
                        {
                            value.Value.AttributeCode,
                            value.Value.Locale,
                            value.Value.Channel,
                            value.Value.Currency,
                            value.Value.SourceAttributeType,
                            value.Value.ReferenceDataName,
                            RawData = value.Value.RawData.HasValue
                                ? value.Value.RawData.Value.GetRawText()
                                : null,
                            value.Value.DisplayValue,
                            DisplayValues = value.Value.DisplayValues?.ToList()
                        }
                })
                .ToList(),
            Categories = categories,
            Assets = assets,
            DesiredOperations = preview.Operations
                .Where(operation => operation.ChangeType !=
                    AkeneoDryRunOperationType.Review)
                .OrderBy(operation => operation.Area, StringComparer.OrdinalIgnoreCase)
                .ThenBy(operation => operation.Target, StringComparer.OrdinalIgnoreCase)
                .ThenBy(operation => operation.AkeneoSource, StringComparer.OrdinalIgnoreCase)
                .Select(operation => new
                {
                    operation.Area,
                    operation.Target,
                    operation.AkeneoSource,
                    operation.ProposedValue,
                    operation.IsRequired
                })
                .ToList()
        };

        return ComputeHash(payload);
    }

    private async Task<object> BuildResolvedAssetFingerprintAsync(
        AkeneoProductSyncContext context,
        CancellationToken cancellationToken)
    {
        if (context.Request.AssetSyncMode == AkeneoCollectionSyncMode.Disabled)
            return Array.Empty<object>();

        var mappings = await assetMappingService
            .GetEffectiveMappingsAsync(context.MappingFamilyCode);
        var currentScope = AkeneoAttributeMappingScopeHelper.NormalizeCurrentScope(
            context.MappingEntityScope,
            context.Source,
            context.SourceEntityType);
        var resolved = new List<object>();

        foreach (var mapping in mappings
                     .Where(mapping => mapping.Enabled &&
                         AkeneoAttributeMappingScopeHelper
                             .NormalizeConfiguredScope(mapping.EntityScopeId)
                             .HasFlag(currentScope))
                     .OrderBy(mapping => mapping.DisplayOrder)
                     .ThenBy(mapping => mapping.Id))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var resolution = await assetResolver.ResolveAsync(
                context,
                mapping,
                cancellationToken);

            if (!resolution.CanReconcile)
                return null;

            resolved.Add(new
            {
                Mapping = new
                {
                    mapping.MappingKey,
                    mapping.Name,
                    mapping.SourceTypeId,
                    mapping.SourceAttributeCode,
                    mapping.FallbackSourceAttributeCode,
                    mapping.AssetFamilyCode,
                    mapping.AssetMediaAttributeCode,
                    mapping.AssetMediaType,
                    mapping.DestinationTypeId,
                    mapping.StorageModeId,
                    mapping.EntityScopeId,
                    mapping.RoleAttributeCode,
                    mapping.RoleValuesCsv,
                    mapping.SortOrderAttributeCode,
                    mapping.AltTextTemplate,
                    mapping.TitleTextTemplate,
                    mapping.SeoFilenameTemplate,
                    mapping.CustomPropertyKey,
                    mapping.DisplayOrder,
                    mapping.MaxAssets
                },
                Assets = resolution.Assets
                    .OrderBy(asset => asset.DisplayOrder)
                    .ThenBy(asset => asset.SourceIdentityHash, StringComparer.OrdinalIgnoreCase)
                    .Select(asset => new
                    {
                        asset.SourceIdentityHash,
                        asset.SourceFingerprint,
                        asset.SourceAttributeCode,
                        asset.AssetFamilyCode,
                        asset.AssetCode,
                        asset.MediaFileCode,
                        asset.ExternalUrl,
                        asset.MimeType,
                        asset.OriginalFileName,
                        asset.AltText,
                        asset.TitleText,
                        asset.SeoFilename,
                        asset.DisplayOrder,
                        asset.SourceUpdatedOnUtc
                    })
                    .ToList()
            });
        }

        return resolved;
    }

    private async Task<IReadOnlyList<object>> BuildCategoryDestinationFingerprintAsync(
        AkeneoProductSyncContext context,
        CancellationToken cancellationToken)
    {
        var categoryCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var code in context.Source.Categories ?? new List<string>())
        {
            if (!string.IsNullOrWhiteSpace(code))
                categoryCodes.Add(code.Trim());
        }

        foreach (var mapped in context
                     .GetMappings(NopTargetType.Category)
                     .Where(value => value.HasValue))
        {
            foreach (var code in AkeneoSyncValueHelper.GetRawItems(mapped))
            {
                if (!string.IsNullOrWhiteSpace(code))
                    categoryCodes.Add(code.Trim());
            }
        }

        var result = new List<object>();

        foreach (var code in categoryCodes.OrderBy(
                     value => value,
                     StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var nopCategoryId = await entityMappingService
                .GetMappedNopEntityIdByAkeneoCodeAsync(
                    AkeneoEntityType.Category,
                    code,
                    NopEntityType.Category);

            result.Add(new
            {
                AkeneoCategoryCode = code,
                NopCategoryId = nopCategoryId
            });
        }

        return result;
    }

    private static object BuildWritePolicyFingerprint(
        AkeneoProductImportRequest request)
    {
        var batchRequest = request as AkeneoProductBatchImportRequest;

        return new
        {
            request.Locale,
            request.Channel,
            request.Currency,
            request.CreateNewProducts,
            request.UpdateExistingProducts,
            ProductFieldMissingValueBehavior =
                (int)request.ProductFieldMissingValueBehavior,
            SeoFieldMissingValueBehavior =
                (int)request.SeoFieldMissingValueBehavior,
            CustomPropertyMissingValueBehavior =
                (int)request.CustomPropertyMissingValueBehavior,
            CategorySyncMode = (int)request.CategorySyncMode,
            SpecificationAttributeSyncMode =
                (int)request.SpecificationAttributeSyncMode,
            ProductAttributeSyncMode = (int)request.ProductAttributeSyncMode,
            AssetSyncMode = (int)request.AssetSyncMode,
            request.CreateMissingProductAttributeValues,
            UnmappedAttributeBehavior = batchRequest == null
                ? (int?)null
                : (int)batchRequest.UnmappedAttributeBehavior
        };
    }

    private static object BuildFamilyMappingFingerprint(
        AkeneoFamilyMapping configuration) => configuration == null
        ? null
        : new
        {
            configuration.AkeneoFamilyCode,
            configuration.AkeneoFamilyVariantCode,
            configuration.Enabled,
            configuration.VariantRelationshipModeId,
            configuration.ProductModelHierarchyModeId,
            configuration.PreserveExistingNopVariantStructure,
            configuration.AssociatedProductAttributeId,
            configuration.AssociatedValueNameTemplate,
            configuration.HideChildProductsWhenRepresentedByParent
        };

    private static object BuildSubModelRuleFingerprint(
        AkeneoFamilySubModelRule rule) => rule == null
        ? null
        : new
        {
            rule.AkeneoAxisAttributeCode,
            rule.TriggerValue,
            rule.VariantAxisAttributeCode,
            rule.VariantTriggerValue,
            rule.VariantRelationshipOverrideModeId,
            rule.MergeAncestorValues,
            rule.DisplayOrder
        };

    private static string ComputeHash(object value)
    {
        var bytes = SHA256.HashData(
            Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(value, SnapshotSerializerOptions)));

        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static void ApplyPreviousStateBinding(
        AkeneoProductImportResult result,
        AkeneoProductSyncState previousState)
    {
        if (previousState == null)
            return;

        result.DestinationKind =
            (AkeneoProductDestinationKind)previousState.DestinationKindId;
        result.NopProductId = previousState.NopProductId;
        result.NopParentProductId = previousState.NopParentProductId;
        result.NopProductAttributeCombinationId =
            previousState.NopProductAttributeCombinationId;
        result.NopProductAttributeValueId =
            previousState.NopProductAttributeValueId;
    }

    private async Task<DateTime> RenewLeaseIfDueAsync(
        AkeneoProductImportRequest request,
        DateTime lastRenewalUtc)
    {
        if (!request.SyncLeaseId.HasValue || request.SyncLeaseId.Value <= 0)
            return lastRenewalUtc;

        var now = DateTime.UtcNow;
        if (now - lastRenewalUtc < LeaseRenewalInterval)
            return lastRenewalUtc;

        await syncLeaseService.RenewByIdAsync(
            request.SyncLeaseId.Value,
            LeaseDuration);

        return now;
    }

    private static TransactionScope CreateUnitTransaction() => new(
        TransactionScopeOption.Required,
        new TransactionOptions
        {
            IsolationLevel = IsolationLevel.ReadCommitted
        },
        TransactionScopeAsyncFlowOption.Enabled);
}
