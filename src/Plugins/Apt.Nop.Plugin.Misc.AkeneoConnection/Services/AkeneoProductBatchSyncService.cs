using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Transactions;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Humanizer;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Data;
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
    IProductAttributeService productAttributeService,
    IAkeneoFamilyMappingService familyMappingService,
    IAkeneoProductModelHierarchyResolver productModelHierarchyResolver,
    IAkeneoVariantRelationshipService variantRelationshipService,
    IRepository<ProductAttributeValue> productAttributeValueRepository,
    IAkeneoProductSyncService productSyncService,
    IAkeneoProductSyncStateService syncStateService,
    IAkeneoVariantRepresentationCleanupService representationCleanupService,
    IAkeneoCatalogReconciliationService catalogReconciliationService,
    IAkeneoSyncLeaseService syncLeaseService)
    : IAkeneoProductBatchSyncService
{
    private static readonly JsonSerializerOptions SnapshotSerializerOptions = new()
    {
        WriteIndented = false
    };

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
        var searchAfter = request.SearchAfter;
        var parentProductCache = new Dictionary<string, Product>(StringComparer.OrdinalIgnoreCase);
        var productModelCache = new Dictionary<string, AkeneoProductDefinition>(StringComparer.OrdinalIgnoreCase);
        var lastLeaseRenewalUtc = DateTime.MinValue;

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                lastLeaseRenewalUtc = await RenewLeaseIfDueAsync(
                    request,
                    lastLeaseRenewalUtc);

                if (HasReachedLimit(request, result))
                {
                    MarkTruncated(request, result);
                    break;
                }

                var page = await akeneoApiClient.GetProductsPageAsync(
                    GetEffectivePageSize(request, result, pageSize),
                    searchAfter,
                    request.SearchJson,
                    cancellationToken);

                if (page?.Items == null || page.Items.Count == 0)
                {
                    result.CompletedAllPages = true;
                    break;
                }

                foreach (var source in page.Items)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    lastLeaseRenewalUtc = await RenewLeaseIfDueAsync(
                        request,
                        lastLeaseRenewalUtc);

                    if (HasReachedLimit(request, result))
                    {
                        MarkTruncated(request, result);
                        break;
                    }

                    result.TotalRead++;
                    var itemResult = CreateInitialResult(source);
                    var snapshot = request.SaveRawPayloadSnapshot
                        ? SerializeSnapshot(source)
                        : null;

                    try
                    {
                        // Warm the model cache BEFORE opening the transaction so no Akeneo
                        // HTTP call happens inside it. After this, every
                        // GetProductModelCachedAsync call for this item is a cache hit.
                        await PrefetchProductModelsAsync(
                            source,
                            productModelCache,
                            cancellationToken);

                        using (var transaction = CreateUnitTransaction())
                        {
                            itemResult = await ReconcileProductUnitAsync(
                                source,
                                request,
                                itemResult,
                                parentProductCache,
                                productModelCache,
                                cancellationToken);

                            if (itemResult.Success &&
                                itemResult.ActionType != SyncItemActionType.Failed)
                            {
                                transaction.Complete();
                            }
                        }

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
                        break;
                    }
                }

                if (!request.ContinueOnError && result.FailedCount > 0)
                    break;

                if (result.WasTruncated)
                    break;

                if (!page.HasNextPage)
                {
                    result.CompletedAllPages = true;
                    break;
                }

                searchAfter = page.SearchAfter;
            }

            if (request.IsAuthoritativeFullRun && result.IsAuthoritative)
            {
                result.ReconciledCount = await catalogReconciliationService
                    .ReconcileUnseenAsync(request, result, cancellationToken);
            }

            result.AddMessage(
                $"Product synchronization completed. Read: {result.TotalRead}, " +
                $"Created: {result.CreatedCount}, Updated: {result.UpdatedCount}, " +
                $"Skipped: {result.SkippedCount}, Failed: {result.FailedCount}, " +
                $"Reconciled: {result.ReconciledCount}.");

            return result;
        }
        catch (OperationCanceledException)
        {
            result.Canceled = true;
            result.AddError("Product synchronization was canceled.");
            return result;
        }
        catch (Exception ex)
        {
            result.AddError(ex.Message);
            return result;
        }
    }

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

            // Warm the model cache BEFORE the transaction so the reconcile unit
            // performs no Akeneo HTTP call while a DB transaction is open.
            await PrefetchProductModelsAsync(
                source,
                productModelCache,
                cancellationToken);

            using (var transaction = CreateUnitTransaction())
            {
                result = await ReconcileProductUnitAsync(
                    source,
                    request,
                    result,
                    parentProductCache,
                    productModelCache,
                    cancellationToken);

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

        result = await ImportProductAsync(
            source,
            request,
            result,
            previousState,
            parentProductCache,
            productModelCache,
            cancellationToken);

        if (!result.Success || result.ActionType == SyncItemActionType.Failed)
            return result;

        await representationCleanupService.CleanupPreviousRepresentationAsync(
            previousState,
            result,
            cancellationToken);

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
                ComputeDesiredStateHash(source));
        }

        return result;
    }

    private async Task<AkeneoProductImportResult> ImportProductAsync(
        AkeneoProductDefinition source,
        AkeneoProductImportRequest request,
        AkeneoProductImportResult result,
        AkeneoProductSyncState previousState,
        IDictionary<string, Product> parentProductCache,
        IDictionary<string, AkeneoProductDefinition> productModelCache,
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
            var standaloneContext = await productSyncService.PrepareAsync(
                source,
                AkeneoEntityType.Product,
                request,
                result,
                cancellationToken);

            await productSyncService.SynchronizeAsync(
                standaloneContext,
                cancellationToken);

            result.DestinationKind = AkeneoProductDestinationKind.NopProduct;
            return result;
        }

        var familyCode = source.Family?.Trim();
        var familyConfiguration = await familyMappingService
            .GetByFamilyCodeAsync(familyCode);

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

        var hierarchyMode = familyConfiguration is { Enabled: true }
            ? familyConfiguration.ProductModelHierarchyMode
            : AkeneoProductModelHierarchyMode.ImmediateParentProductModel;

        var hierarchy = productModelHierarchyResolver.Resolve(
            source,
            ancestors,
            hierarchyMode);

        // Prepare the unflattened leaf first so a standalone submodel rule can
        // still opt out of inherited values.
        var rawLeafContext = await productSyncService.PrepareAsync(
            source,
            AkeneoEntityType.Product,
            request,
            result,
            cancellationToken);

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
                var existingMode = await ClassifyExistingLeafStructureAsync(
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
                    var effectiveContext = await productSyncService.PrepareAsync(
                        standaloneSource,
                        AkeneoEntityType.Product,
                        request,
                        result,
                        familyCode,
                        AkeneoAttributeMappingEntityScope.StandaloneProduct,
                        cancellationToken);

                    if (!result.Success)
                    {
                        result.ActionType = SyncItemActionType.Failed;
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
            : await productSyncService.PrepareAsync(
                effectiveSource,
                AkeneoEntityType.Product,
                request,
                result,
                cancellationToken);

        if (!result.Success)
        {
            result.ActionType = SyncItemActionType.Failed;
            return result;
        }

        var parentSync = await ResolveOrCreateParentProductAsync(
            hierarchy.EffectiveParentProductModel,
            familyCode,
            request,
            result,
            parentProductCache,
            cancellationToken);

        var parentProduct = parentSync?.Product;

        if (parentProduct == null)
        {
            result.ActionType = SyncItemActionType.Failed;
            return result;
        }

        var variantContext = await BuildVariantImportContextAsync(
            effectiveSource,
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

        return rules.FirstOrDefault(rule => RuleMatches(rule, leaf, subModel));
    }

    private static bool RuleMatches(
        AkeneoFamilySubModelRule rule,
        AkeneoProductDefinition leaf,
        AkeneoProductDefinition subModel)
    {
        var hasSubModelCondition =
            !string.IsNullOrWhiteSpace(rule.AkeneoAxisAttributeCode);
        var hasVariantCondition =
            !string.IsNullOrWhiteSpace(rule.VariantAxisAttributeCode);

        if (!hasSubModelCondition && !hasVariantCondition)
            return false;

        if (hasSubModelCondition &&
            !ValueMatches(
                GetAkeneoAttributeValue(subModel, rule.AkeneoAxisAttributeCode),
                rule.TriggerValue))
        {
            return false;
        }

        return !hasVariantCondition || ValueMatches(
            GetAkeneoAttributeValue(leaf, rule.VariantAxisAttributeCode),
            rule.VariantTriggerValue);
    }

    private static bool ValueMatches(string actual, string trigger) =>
        !string.IsNullOrWhiteSpace(actual) &&
        string.Equals(
            actual.Trim(),
            trigger?.Trim(),
            StringComparison.OrdinalIgnoreCase);

    private static string GetAkeneoAttributeValue(
        AkeneoProductDefinition item,
        string attributeCode)
    {
        if (string.IsNullOrWhiteSpace(attributeCode) ||
            item?.Values.ValueKind != JsonValueKind.Object ||
            !item.Values.TryGetProperty(attributeCode, out var entries) ||
            entries.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("data", out var data))
                continue;

            return data.ValueKind switch
            {
                JsonValueKind.String => data.GetString(),
                JsonValueKind.Number => data.GetRawText(),
                _ => null
            };
        }

        return null;
    }

    private async Task<AkeneoVariantRelationshipMode?> ClassifyExistingLeafStructureAsync(
        Product product,
        string sku)
    {
        if (product == null)
            return null;

        if (product.ParentGroupedProductId != 0)
            return AkeneoVariantRelationshipMode.GroupedProducts;

        if (!string.IsNullOrWhiteSpace(sku))
        {
            var combination = await productAttributeService
                .GetProductAttributeCombinationBySkuAsync(sku);

            if (combination != null)
                return AkeneoVariantRelationshipMode.ProductAttributeCombinations;
        }

        var isAssociated = await productAttributeValueRepository.Table.AnyAsync(value =>
            value.AttributeValueTypeId == (int)AttributeValueType.AssociatedToProduct &&
            value.AssociatedProductId == product.Id);

        return isAssociated
            ? AkeneoVariantRelationshipMode.AssociatedToProductAttributeValue
            : AkeneoVariantRelationshipMode.None;
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
            result.Warnings.Any();

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
        result.Warnings.Any();

    private static void ClearRolledBackDestination(
        AkeneoProductImportResult result)
    {
        result.NopProductId = 0;
        result.NopParentProductId = null;
        result.NopProductAttributeCombinationId = null;
        result.NopProductAttributeValueId = null;
    }

    private static string GetIdentity(AkeneoProductImportResult result) =>
        result.Sku ??
        result.AkeneoIdentifier ??
        result.AkeneoProductUuid ??
        "(unknown product)";

    private static string SerializeSnapshot(AkeneoProductDefinition source) =>
        JsonSerializer.Serialize(source, SnapshotSerializerOptions).Truncate(12000);

    private static string ComputeDesiredStateHash(
        AkeneoProductDefinition source)
    {
        var bytes = SHA256.HashData(
            Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(source, SnapshotSerializerOptions)));

        return Convert.ToHexString(bytes).ToLowerInvariant();
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