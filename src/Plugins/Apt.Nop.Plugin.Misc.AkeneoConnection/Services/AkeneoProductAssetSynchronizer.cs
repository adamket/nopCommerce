using System.Text.Json;
using System.Text.Json.Serialization;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services.DryRun;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Assets;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Media;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Media;
using static Apt.Nop.Plugin.Misc.AkeneoConnection.Services.DryRun.AkeneoDryRunPlanHelper;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

/// <summary>
/// Synchronizes Akeneo media and Asset Manager collections after the product
/// exists but before custom scalar properties are applied.
/// </summary>
public sealed class AkeneoProductAssetSynchronizer(
    IAkeneoAssetMappingService assetMappingService,
    IAkeneoManagedAssetService managedAssetService,
    IAkeneoAssetResolver assetResolver,
    IAkeneoApiClient apiClient,
    IAkeneoExternalAssetDownloader externalAssetDownloader,
    IPictureService pictureService,
    IVideoService videoService,
    IProductService productService,
    IGenericAttributeService genericAttributeService)
    : IAkeneoProductSectionSynchronizer,
      IAkeneoSectionDryRunPlanProvider,
      IAkeneoAssetDryRunPlanProvider
{
    public int Order => 550;

    public async Task SynchronizeAsync(
        AkeneoProductSyncContext context,
        CancellationToken cancellationToken = default)
    {
        if (context.Product == null || context.Request.AssetSyncMode == AkeneoCollectionSyncMode.Disabled)
            return;

        // Resolve the complete desired state before making destructive changes.
        // This is especially important for ReplaceAll: a temporary Akeneo error,
        // missing locale/channel match, or invalid external asset must never clear
        // the product gallery before the replacement set is known to be safe.

        var desired = await ResolveDesiredAsync(context, context.Result.AddWarning, cancellationToken);
        var activeMappings = desired.ActiveMappings;
        var resolvedMappings = desired.ResolvedMappings;
        var authoritativeResolutionFailed = desired.AuthoritativeResolutionFailed;

        if (context.Request.AssetSyncMode == AkeneoCollectionSyncMode.ReplaceAll &&
            authoritativeResolutionFailed)
        {
            context.Result.AddWarning(
                "Asset Replace all was skipped because one or more mappings could not be resolved safely. Existing nopCommerce media was preserved.");
            return;
        }

        var allMappingsApplied = true;
        foreach (var resolved in resolvedMappings)
        {
            try
            {
                allMappingsApplied &= await ApplyMappingAsync(
                    context,
                    resolved.Mapping,
                    resolved.Assets,
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                allMappingsApplied = false;
                context.Result.AddWarning(
                    $"Asset mapping '{resolved.Mapping.Name}' could not be applied: {ex.Message}");
            }
        }

        if (context.Request.AssetSyncMode == AkeneoCollectionSyncMode.ReplaceAll)
        {
            if (!allMappingsApplied)
            {
                context.Result.AddWarning(
                    "Asset Replace all cleanup was skipped because one or more desired assets could not be applied. Existing nopCommerce media was preserved alongside any successfully synchronized assets.");
                return;
            }

            await FinalizeReplaceAllAsync(
                context,
                activeMappings,
                resolvedMappings,
                cancellationToken);
        }
        else if (context.Request.AssetSyncMode ==
                 AkeneoCollectionSyncMode.ReplaceManaged)
        {
            await RemoveOrphanedMappingsAsync(
                context,
                activeMappings.Select(mapping => mapping.MappingKey),
                cancellationToken);
        }
    }

    public async Task PrepareAsync(AkeneoProductSyncContext context, CancellationToken cancellationToken = default)
    {
        if (context.Request.AssetSyncMode == AkeneoCollectionSyncMode.Disabled)
            return;

        var desired = await ResolveDesiredAsync(context, warningSink: null, cancellationToken: cancellationToken);

        foreach (var resolved in desired.ResolvedMappings)
        {
            if ((AkeneoAssetDestinationType)resolved.Mapping.DestinationTypeId != AkeneoAssetDestinationType.ProductPicture)
                continue; // videos are URL references, no binary
            if ((AkeneoAssetStorageMode)resolved.Mapping.StorageModeId != AkeneoAssetStorageMode.ImportIntoNopCommerce)
                continue;

            // Fingerprint skip for updates so unchanged images aren't re-downloaded every run.
            IList<AkeneoManagedAsset> existing = context.ExistingProduct is { Id: > 0 }
                ? await managedAssetService.GetByProductAndMappingAsync(context.ExistingProduct.Id, resolved.Mapping.MappingKey)
                : Array.Empty<AkeneoManagedAsset>();

            foreach (var asset in resolved.Assets)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var key = BuildBinaryCacheKey(asset);
                if (key == null || context.PreloadedAssetBinaries.ContainsKey(key))
                    continue;

                var managed = existing.FirstOrDefault(m =>
                    string.Equals(m.SourceIdentityHash, asset.SourceIdentityHash, StringComparison.OrdinalIgnoreCase));
                var binaryChanged = managed == null ||
                    !string.Equals(managed.SourceFingerprint, asset.SourceFingerprint, StringComparison.OrdinalIgnoreCase);
                if (!binaryChanged)
                    continue;

                try
                {
                    context.PreloadedAssetBinaries[key] = await DownloadFromSourceAsync(asset, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Non-fatal: write phase falls back to an inline download and warns there.
                }
            }
        }
    }


    /// <summary>
    /// Builds a read-only asset plan from the same desired-state resolver used
    /// by the write path. No media is downloaded and no nopCommerce data is
    /// changed.
    /// </summary>
    public async Task PlanAsync(
        AkeneoProductSyncContext context,
        AkeneoProductMappingPreviewModel model,
        CancellationToken cancellationToken = default)
    {
        if (context.Request.AssetSyncMode == AkeneoCollectionSyncMode.Disabled)
        {
            model.CoverageNotes.Add(
                "Asset synchronization is disabled by the saved sync profile.");
            return;
        }

        var desired = await ResolveDesiredAsync(
            context,
            warning => model.Warnings.Add(warning),
            cancellationToken);

        if (desired.ActiveMappings.Count == 0)
            return;

        var resolvedById = desired.ResolvedMappings.ToDictionary(
            item => item.Mapping.Id);

        foreach (var mapping in desired.ActiveMappings)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!resolvedById.TryGetValue(mapping.Id, out var resolved))
            {
                AddReviewOperation(
                    model,
                    "Assets",
                    GetMappingDisplayName(mapping),
                    GetMappingSource(mapping),
                    "The asset source could not be resolved authoritatively. The import preserves existing media when a safe desired state cannot be built.");
                continue;
            }

            await PlanResolvedMappingAsync(
                context,
                model,
                resolved,
                cancellationToken);
        }

        if (context.ExistingProduct is { Id: > 0 })
        {
            if (context.Request.AssetSyncMode == AkeneoCollectionSyncMode.ReplaceManaged)
            {
                await PlanOrphanedMappingCleanupAsync(
                    context,
                    model,
                    desired.ActiveMappings,
                    cancellationToken);
            }
            else if (context.Request.AssetSyncMode == AkeneoCollectionSyncMode.ReplaceAll &&
                     !desired.AuthoritativeResolutionFailed)
            {
                // The real write path performs no cleanup at all when an
                // authoritative Replace All desired state cannot be resolved.
                await PlanOrphanedMappingCleanupAsync(
                    context,
                    model,
                    desired.ActiveMappings,
                    cancellationToken);
                await PlanReplaceAllCollectionCleanupAsync(
                    context,
                    model,
                    desired.ActiveMappings,
                    desired.ResolvedMappings,
                    cancellationToken);
            }
        }

        if (desired.AuthoritativeResolutionFailed &&
            context.Request.AssetSyncMode == AkeneoCollectionSyncMode.ReplaceAll)
        {
            AddReviewOperation(
                model,
                "Assets",
                "Authoritative replacement",
                "Asset mappings",
                "Replace All cleanup will be skipped because at least one asset mapping could not be resolved safely. Existing media is preserved.");
        }

        model.CoverageNotes.Add(
            "Asset operations are compared from Akeneo asset metadata, plugin ownership records, and current nopCommerce media metadata. Picture binaries are not downloaded during preview; import still validates the file, MIME type, and download before applying a picture change.");
    }

    private async Task PlanResolvedMappingAsync(
        AkeneoProductSyncContext context,
        AkeneoProductMappingPreviewModel model,
        ResolvedAssetMapping resolved,
        CancellationToken cancellationToken)
    {
        var mapping = resolved.Mapping;
        var product = context.ExistingProduct;
        var existing = product is { Id: > 0 }
            ? (await managedAssetService.GetByProductAndMappingAsync(
                product.Id,
                mapping.MappingKey)).ToList()
            : new List<AkeneoManagedAsset>();

        var destinationType = (AkeneoAssetDestinationType)mapping.DestinationTypeId;
        var customPropertyKey = destinationType == AkeneoAssetDestinationType.CustomProperty
            ? GetCustomPropertyKey(mapping)
            : null;

        var incompatible = existing.Where(item =>
                item.DestinationTypeId != mapping.DestinationTypeId ||
                (destinationType == AkeneoAssetDestinationType.CustomProperty &&
                 !string.Equals(
                     item.DestinationKey,
                     customPropertyKey,
                     StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var stale in incompatible)
        {
            AddOperation(
                model,
                "Assets",
                DescribeManagedAsset(stale),
                GetMappingSource(mapping),
                DescribeManagedAssetValue(stale),
                null,
                AkeneoDryRunOperationType.Remove,
                $"The saved mapping now targets {destinationType}; the incompatible plugin-managed destination is removed before the desired assets are applied.");
            existing.Remove(stale);
        }

        if (destinationType == AkeneoAssetDestinationType.CustomProperty)
        {
            await PlanCustomPropertyMappingAsync(
                context,
                model,
                mapping,
                resolved.Assets,
                cancellationToken);

            if (context.Request.AssetSyncMode == AkeneoCollectionSyncMode.ReplaceManaged)
            {
                var rmDesiredKeys = resolved.Assets
                    .Select(item => item.SourceIdentityHash)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var stale in existing.Where(item =>
                             !rmDesiredKeys.Contains(item.SourceIdentityHash)))
                {
                    AddOperation(
                        model,
                        "Assets",
                        DescribeManagedAsset(stale),
                        GetMappingSource(mapping),
                        DescribeManagedAssetValue(stale),
                        null,
                        AkeneoDryRunOperationType.Remove,
                        "Replace Managed removes this stale plugin ownership record after the custom-property payload is rewritten without it.");
                }
            }

            return;
        }

        foreach (var asset in resolved.Assets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var managed = existing.FirstOrDefault(item =>
                string.Equals(
                    item.SourceIdentityHash,
                    asset.SourceIdentityHash,
                    StringComparison.OrdinalIgnoreCase));

            await PlanDestinationAssetAsync(
                context,
                model,
                asset,
                managed,
                cancellationToken);
        }

        if (context.Request.AssetSyncMode != AkeneoCollectionSyncMode.ReplaceManaged)
            return;

        var desiredKeys = resolved.Assets
            .Select(item => item.SourceIdentityHash)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var stale in existing.Where(item =>
                     !desiredKeys.Contains(item.SourceIdentityHash)))
        {
            AddOperation(
                model,
                "Assets",
                DescribeManagedAsset(stale),
                GetMappingSource(mapping),
                DescribeManagedAssetValue(stale),
                null,
                AkeneoDryRunOperationType.Remove,
                "Replace Managed removes this stale destination because it is owned by this asset mapping and is no longer in the desired Akeneo set.");
        }
    }

    private async Task PlanDestinationAssetAsync(
        AkeneoProductSyncContext context,
        AkeneoProductMappingPreviewModel model,
        AkeneoResolvedAsset asset,
        AkeneoManagedAsset managed,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var destinationType = (AkeneoAssetDestinationType)asset.Mapping.DestinationTypeId;
        var target = destinationType switch
        {
            AkeneoAssetDestinationType.ProductPicture =>
                $"Product picture: {GetAssetDisplayName(asset)}",
            AkeneoAssetDestinationType.ProductVideo =>
                $"Product video: {GetAssetDisplayName(asset)}",
            _ => GetAssetDisplayName(asset)
        };

        if (destinationType == AkeneoAssetDestinationType.ProductPicture &&
            (AkeneoAssetStorageMode)asset.Mapping.StorageModeId !=
                AkeneoAssetStorageMode.ImportIntoNopCommerce)
        {
            AddReviewOperation(
                model,
                "Assets",
                target,
                GetMappingSource(asset.Mapping),
                "The mapping targets Product Picture but is configured as an external reference, so the write pipeline will reject it.",
                current: managed == null ? null : DescribeManagedAssetValue(managed),
                proposed: DescribeResolvedAssetValue(asset));
            return;
        }

        if (managed == null)
        {
            AddOperation(
                model,
                "Assets",
                target,
                GetMappingSource(asset.Mapping),
                null,
                DescribeResolvedAssetValue(asset),
                context.ExistingProduct == null
                    ? AkeneoDryRunOperationType.Create
                    : AkeneoDryRunOperationType.Add,
                destinationType == AkeneoAssetDestinationType.ProductPicture
                    ? "The image will be downloaded and validated before the picture and product-picture relationship are created."
                    : "The video and product-video relationship will be created from the resolved external URL.");
            return;
        }

        var changed =
            !string.Equals(
                managed.SourceFingerprint,
                asset.SourceFingerprint,
                StringComparison.OrdinalIgnoreCase) ||
            managed.DisplayOrder != asset.DisplayOrder;
        var detailParts = new List<string>();

        if (!string.Equals(
                managed.SourceFingerprint,
                asset.SourceFingerprint,
                StringComparison.OrdinalIgnoreCase))
        {
            detailParts.Add("source fingerprint changed");
        }

        if (managed.DisplayOrder != asset.DisplayOrder)
            detailParts.Add($"display order {managed.DisplayOrder} → {asset.DisplayOrder}");

        if (destinationType == AkeneoAssetDestinationType.ProductPicture)
        {
            Picture picture = null;
            ProductPicture relation = null;

            if (managed.NopPictureId > 0)
                picture = await pictureService.GetPictureByIdAsync(managed.NopPictureId.Value);
            if (managed.NopProductPictureId > 0)
                relation = await productService.GetProductPictureByIdAsync(managed.NopProductPictureId.Value);

            if (picture == null || relation == null)
            {
                changed = true;
                detailParts.Add("managed nopCommerce picture or relationship is missing");
            }
            else
            {
                var seoName = await ResolveSeoFilenameAsync(asset);
                if (!string.Equals(
                        picture.SeoFilename,
                        seoName,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(
                        picture.AltAttribute,
                        asset.AltText,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(
                        picture.TitleAttribute,
                        asset.TitleText,
                        StringComparison.OrdinalIgnoreCase))
                {
                    changed = true;
                    detailParts.Add("picture metadata changed");
                }

                if (relation.PictureId != picture.Id ||
                    relation.DisplayOrder != asset.DisplayOrder)
                {
                    changed = true;
                    detailParts.Add("product-picture relationship changed");
                }
            }
        }
        else if (destinationType == AkeneoAssetDestinationType.ProductVideo)
        {
            Video video = null;
            ProductVideo relation = null;

            if (managed.NopVideoId > 0)
                video = await videoService.GetVideoByIdAsync(managed.NopVideoId.Value);
            if (managed.NopProductVideoId > 0)
                relation = await productService.GetProductVideoByIdAsync(managed.NopProductVideoId.Value);

            if (video == null || relation == null)
            {
                changed = true;
                detailParts.Add("managed nopCommerce video or relationship is missing");
            }
            else
            {
                if (!string.Equals(
                        video.VideoUrl,
                        asset.ExternalUrl,
                        StringComparison.OrdinalIgnoreCase))
                {
                    changed = true;
                    detailParts.Add("video URL changed");
                }

                if (relation.VideoId != video.Id ||
                    relation.DisplayOrder != asset.DisplayOrder)
                {
                    changed = true;
                    detailParts.Add("product-video relationship changed");
                }
            }
        }

        AddOperation(
            model,
            "Assets",
            target,
            GetMappingSource(asset.Mapping),
            DescribeManagedAssetValue(managed),
            DescribeResolvedAssetValue(asset),
            changed
                ? AkeneoDryRunOperationType.Update
                : AkeneoDryRunOperationType.NoChange,
            detailParts.Count == 0
                ? "The managed destination matches the resolved Akeneo asset metadata."
                : string.Join("; ", detailParts) + ".");
    }

    private async Task PlanCustomPropertyMappingAsync(
        AkeneoProductSyncContext context,
        AkeneoProductMappingPreviewModel model,
        AkeneoAssetMapping mapping,
        IReadOnlyList<AkeneoResolvedAsset> desired,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var key = GetCustomPropertyKey(mapping);
        var current = context.ExistingProduct is { Id: > 0 }
            ? await genericAttributeService.GetAttributeAsync<string>(
                context.ExistingProduct,
                key)
            : null;

        if (!TryBuildCustomPropertyPayload(
                current,
                desired,
                context.Request.AssetSyncMode,
                out var desiredJson,
                out var payloadCount,
                out var warning))
        {
            AddReviewOperation(
                model,
                "Assets",
                $"Custom property: {key}",
                GetMappingSource(mapping),
                warning,
                current: SummarizeJson(current),
                proposed: $"{desired.Count} resolved asset reference(s)");
            return;
        }

        var type = context.ExistingProduct == null
            ? string.IsNullOrWhiteSpace(desiredJson)
                ? AkeneoDryRunOperationType.NoChange
                : AkeneoDryRunOperationType.Create
            : DetermineScalarChangeType(current, desiredJson, ignoreCase: true);

        AddOperation(
            model,
            "Assets",
            $"Custom property: {key}",
            GetMappingSource(mapping),
            SummarizeJson(current),
            SummarizeJson(desiredJson),
            type,
            $"The property will contain {payloadCount} asset reference(s) using the same JSON payload builder as the write path.");
    }

    private async Task PlanOrphanedMappingCleanupAsync(
        AkeneoProductSyncContext context,
        AkeneoProductMappingPreviewModel model,
        IReadOnlyCollection<AkeneoAssetMapping> activeMappings,
        CancellationToken cancellationToken)
    {
        var active = activeMappings
            .Select(mapping => mapping.MappingKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var managedAssets = await managedAssetService.GetByProductAsync(
            context.ExistingProduct.Id);

        foreach (var orphan in managedAssets.Where(item =>
                     !active.Contains(item.AssetMappingKey)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddOperation(
                model,
                "Assets",
                DescribeManagedAsset(orphan),
                "Deleted or disabled asset mapping",
                DescribeManagedAssetValue(orphan),
                null,
                AkeneoDryRunOperationType.Remove,
                "The destination is plugin-managed but its asset mapping is no longer active for this product.");
        }
    }

    private async Task PlanReplaceAllCollectionCleanupAsync(
        AkeneoProductSyncContext context,
        AkeneoProductMappingPreviewModel model,
        IReadOnlyCollection<AkeneoAssetMapping> activeMappings,
        IReadOnlyCollection<ResolvedAssetMapping> resolvedMappings,
        CancellationToken cancellationToken)
    {
        var managedAssets = await managedAssetService.GetByProductAsync(
            context.ExistingProduct.Id);
        var desiredByMapping = resolvedMappings.ToDictionary(
            item => item.Mapping.MappingKey,
            item => item.Assets
                .Select(asset => asset.SourceIdentityHash)
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        var mappingByKey = activeMappings.ToDictionary(
            mapping => mapping.MappingKey,
            StringComparer.OrdinalIgnoreCase);
        var desiredManaged = managedAssets.Where(item =>
                mappingByKey.TryGetValue(item.AssetMappingKey, out var mapping) &&
                item.DestinationTypeId == mapping.DestinationTypeId &&
                desiredByMapping.TryGetValue(item.AssetMappingKey, out var identities) &&
                identities.Contains(item.SourceIdentityHash))
            .ToList();
        var destinationTypes = activeMappings
            .Select(mapping => (AkeneoAssetDestinationType)mapping.DestinationTypeId)
            .ToHashSet();

        if (destinationTypes.Contains(AkeneoAssetDestinationType.ProductPicture))
        {
            var keep = desiredManaged
                .Where(item =>
                    item.DestinationTypeId == (int)AkeneoAssetDestinationType.ProductPicture &&
                    item.NopProductPictureId > 0)
                .Select(item => item.NopProductPictureId.Value)
                .ToHashSet();

            foreach (var relation in await productService
                         .GetProductPicturesByProductIdAsync(context.ExistingProduct.Id))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (keep.Contains(relation.Id))
                    continue;

                AddOperation(
                    model,
                    "Assets",
                    $"Product picture relationship #{relation.Id}",
                    "Replace All policy",
                    $"Picture #{relation.PictureId}, display order {relation.DisplayOrder}",
                    null,
                    AkeneoDryRunOperationType.Remove,
                    "Replace All removes every current product picture that is not part of the completely resolved desired set, including unmanaged pictures.");
            }
        }

        if (destinationTypes.Contains(AkeneoAssetDestinationType.ProductVideo))
        {
            var keep = desiredManaged
                .Where(item =>
                    item.DestinationTypeId == (int)AkeneoAssetDestinationType.ProductVideo &&
                    item.NopProductVideoId > 0)
                .Select(item => item.NopProductVideoId.Value)
                .ToHashSet();

            foreach (var relation in await productService
                         .GetProductVideosByProductIdAsync(context.ExistingProduct.Id))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (keep.Contains(relation.Id))
                    continue;

                AddOperation(
                    model,
                    "Assets",
                    $"Product video relationship #{relation.Id}",
                    "Replace All policy",
                    $"Video #{relation.VideoId}, display order {relation.DisplayOrder}",
                    null,
                    AkeneoDryRunOperationType.Remove,
                    "Replace All removes every current product video that is not part of the completely resolved desired set, including unmanaged videos.");
            }
        }
    }

    private static bool TryBuildCustomPropertyPayload(
        string current,
        IReadOnlyList<AkeneoResolvedAsset> desired,
        AkeneoCollectionSyncMode syncMode,
        out string desiredJson,
        out int payloadCount,
        out string warning)
    {
        warning = null;
        desiredJson = null;
        payloadCount = 0;

        if (desired.Count == 0 && syncMode == AkeneoCollectionSyncMode.Merge)
        {
            desiredJson = current;
            if (!string.IsNullOrWhiteSpace(current))
            {
                try
                {
                    payloadCount = JsonSerializer.Deserialize<List<ManagedAssetReferencePayload>>(
                        current,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })?.Count ?? 0;
                }
                catch (JsonException)
                {
                    // Merge with no desired assets leaves the current value untouched.
                }
            }
            return true;
        }

        var payloadByIdentity = new Dictionary<string, ManagedAssetReferencePayload>(
            StringComparer.OrdinalIgnoreCase);

        if (syncMode == AkeneoCollectionSyncMode.Merge &&
            !string.IsNullOrWhiteSpace(current))
        {
            try
            {
                var currentPayload = JsonSerializer.Deserialize<
                    List<ManagedAssetReferencePayload>>(
                    current,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }) ?? new List<ManagedAssetReferencePayload>();

                foreach (var item in currentPayload)
                    payloadByIdentity[GetPayloadIdentity(item)] = item;
            }
            catch (JsonException ex)
            {
                warning =
                    $"The existing managed asset property contains invalid JSON and will be preserved: {ex.Message}";
                return false;
            }
        }

        foreach (var asset in desired)
        {
            var item = ManagedAssetReferencePayload.From(asset);
            payloadByIdentity[GetPayloadIdentity(item)] = item;
        }

        var payload = payloadByIdentity.Values
            .OrderBy(item => item.DisplayOrder)
            .ThenBy(
                item => item.AssetCode ?? item.MediaFileCode ?? item.Url,
                StringComparer.OrdinalIgnoreCase)
            .ToList();

        payloadCount = payload.Count;
        desiredJson = payload.Count == 0
            ? null
            : JsonSerializer.Serialize(payload);
        return true;
    }

    private static string GetMappingDisplayName(AkeneoAssetMapping mapping) =>
        mapping.Name?.Trim() ??
        mapping.MappingKey?.Trim() ??
        $"Asset mapping #{mapping.Id}";

    private static string GetMappingSource(AkeneoAssetMapping mapping)
    {
        return string.Join(
            " → fallback ",
            new[]
            {
                mapping.SourceAttributeCode?.Trim(),
                mapping.FallbackSourceAttributeCode?.Trim()
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string GetAssetDisplayName(AkeneoResolvedAsset asset) =>
        asset.OriginalFileName?.Trim() ??
        asset.AssetCode?.Trim() ??
        asset.MediaFileCode?.Trim() ??
        asset.ExternalUrl?.Trim() ??
        asset.SourceIdentityHash?.Trim() ??
        "Akeneo asset";

    private static string DescribeResolvedAssetValue(AkeneoResolvedAsset asset)
    {
        var identity = GetAssetDisplayName(asset);
        return $"{identity}; display order {asset.DisplayOrder}";
    }

    private static string DescribeManagedAsset(AkeneoManagedAsset asset)
    {
        var destination = (AkeneoAssetDestinationType)asset.DestinationTypeId;
        var identity = asset.AssetCode ?? asset.MediaFileCode ?? asset.SourceUrl ?? asset.SourceIdentityHash;
        return $"{destination}: {identity}";
    }

    private static string DescribeManagedAssetValue(AkeneoManagedAsset asset) =>
        $"{asset.AssetCode ?? asset.MediaFileCode ?? asset.SourceUrl ?? asset.SourceIdentityHash}; display order {asset.DisplayOrder}";

    private static string SummarizeJson(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        const int limit = 240;
        return value.Length <= limit
            ? value
            : value[..limit] + "…";
    }

    private static string BuildBinaryCacheKey(AkeneoResolvedAsset asset)
    {
        // Mirror the source selection in DownloadFromSourceAsync so both phases agree.
        var source =
            !string.IsNullOrWhiteSpace(asset.ExternalUrl) ? "ext:" + asset.ExternalUrl :
            (AkeneoAssetSourceType)asset.Mapping.SourceTypeId == AkeneoAssetSourceType.ProductMediaAttribute ? "media:" + asset.MediaFileCode :
            !string.IsNullOrWhiteSpace(asset.DownloadUrl) ? "url:" + asset.DownloadUrl :
            !string.IsNullOrWhiteSpace(asset.MediaFileCode) ? "asset:" + asset.MediaFileCode :
            null;

        if (source == null)
            return null;

        return string.IsNullOrWhiteSpace(asset.SourceFingerprint) ? source : source + "|" + asset.SourceFingerprint;
    }

    private async Task<AssetDesiredState> ResolveDesiredAsync(
        AkeneoProductSyncContext context,
        Action<string> warningSink,
        CancellationToken cancellationToken)
    {
        var mappings = await assetMappingService.GetEffectiveMappingsAsync(context.MappingFamilyCode);
        var currentScope = AkeneoAttributeMappingScopeHelper.NormalizeCurrentScope(
            context.MappingEntityScope, context.Source, context.SourceEntityType);

        var activeMappings = mappings.Where(mapping =>
                mapping.Enabled &&
                AkeneoAttributeMappingScopeHelper
                    .NormalizeConfiguredScope(mapping.EntityScopeId)
                    .HasFlag(currentScope))
            .ToList();

        var resolvedMappings = new List<ResolvedAssetMapping>();
        var authoritativeResolutionFailed = false;

        foreach (var mapping in activeMappings)
        {
            try
            {
                var resolution = await assetResolver.ResolveAsync(context, mapping, cancellationToken);
                if (!string.IsNullOrWhiteSpace(resolution.Warning))
                    warningSink?.Invoke(resolution.Warning);
                if (!resolution.CanReconcile)
                {
                    authoritativeResolutionFailed = true;
                    continue;
                }
                resolvedMappings.Add(new ResolvedAssetMapping(mapping, resolution.Assets));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                authoritativeResolutionFailed = true;
                warningSink?.Invoke(
                    $"Asset mapping '{mapping.Name}' could not be resolved: {ex.Message}");
            }
        }

        return new AssetDesiredState(activeMappings, resolvedMappings, authoritativeResolutionFailed);
    }


    private async Task<bool> ApplyMappingAsync(
        AkeneoProductSyncContext context,
        AkeneoAssetMapping mapping,
        IReadOnlyList<AkeneoResolvedAsset> desired,
        CancellationToken cancellationToken)
    {
        var profileId = context.Request.SyncProfileId.GetValueOrDefault();
        var existing = await managedAssetService.GetByProductAndMappingAsync(
            context.Product.Id,
            mapping.MappingKey);
        var desiredKeys = desired
            .Select(item => item.SourceIdentityHash)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var changed = await RemoveIncompatibleExistingAsync(
            context,
            mapping,
            existing);

        var complete = true;
        if ((AkeneoAssetDestinationType)mapping.DestinationTypeId ==
            AkeneoAssetDestinationType.CustomProperty)
        {
            var outcome = await ApplyCustomPropertyAsync(
                context,
                mapping,
                desired,
                existing);
            changed |= outcome.Changed;
            complete &= outcome.Complete;
        }
        else
        {
            foreach (var asset in desired)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var managed = existing.FirstOrDefault(item =>
                    string.Equals(
                        item.SourceIdentityHash,
                        asset.SourceIdentityHash,
                        StringComparison.OrdinalIgnoreCase));

                var outcome = await ApplyDestinationAssetAsync(
                    context,
                    asset,
                    managed,
                    profileId,
                    cancellationToken);
                changed |= outcome.Changed;
                complete &= outcome.Complete;
            }
        }

        // Replace all cleanup is deliberately deferred until every active
        // mapping has been applied successfully. This prevents a failed download
        // late in the run from destroying the existing product gallery.
        if (complete &&
            context.Request.AssetSyncMode ==
                AkeneoCollectionSyncMode.ReplaceManaged)
        {
            foreach (var stale in existing.Where(item =>
                         !desiredKeys.Contains(item.SourceIdentityHash)).ToList())
            {
                changed |= await DeleteManagedDestinationAsync(stale);
                await managedAssetService.DeleteAsync(stale);
            }
        }

        if (changed)
            context.MarkChanged();

        return complete;
    }

    private async Task<bool> RemoveIncompatibleExistingAsync(
        AkeneoProductSyncContext context,
        AkeneoAssetMapping mapping,
        IList<AkeneoManagedAsset> existing)
    {
        var destinationType = (AkeneoAssetDestinationType)mapping.DestinationTypeId;
        var customPropertyKey = destinationType == AkeneoAssetDestinationType.CustomProperty
            ? GetCustomPropertyKey(mapping)
            : null;
        var changed = false;
        var clearedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var managed in existing.Where(item =>
                     item.DestinationTypeId != mapping.DestinationTypeId ||
                     (destinationType == AkeneoAssetDestinationType.CustomProperty &&
                      !string.Equals(
                          item.DestinationKey,
                          customPropertyKey,
                          StringComparison.OrdinalIgnoreCase)))
                 .ToList())
        {
            if ((AkeneoAssetDestinationType)managed.DestinationTypeId ==
                    AkeneoAssetDestinationType.CustomProperty &&
                !string.IsNullOrWhiteSpace(managed.DestinationKey) &&
                clearedKeys.Add(managed.DestinationKey))
            {
                await genericAttributeService.SaveAttributeAsync<string>(
                    context.Product,
                    managed.DestinationKey,
                    null);
                changed = true;
            }
            else
            {
                changed |= await DeleteManagedDestinationAsync(managed);
            }

            await managedAssetService.DeleteAsync(managed);
            existing.Remove(managed);
        }

        return changed;
    }

    private static string GetCustomPropertyKey(AkeneoAssetMapping mapping) =>
        string.IsNullOrWhiteSpace(mapping.CustomPropertyKey)
            ? $"Apt.Akeneo.Asset.{mapping.MappingKey}"
            : mapping.CustomPropertyKey.Trim();

    private async Task<AssetApplyResult> ApplyDestinationAssetAsync(
        AkeneoProductSyncContext context,
        AkeneoResolvedAsset asset,
        AkeneoManagedAsset managed,
        int profileId,
        CancellationToken cancellationToken)
    {
        return (AkeneoAssetDestinationType)asset.Mapping.DestinationTypeId switch
        {
            AkeneoAssetDestinationType.ProductPicture =>
                await ApplyPictureAsync(
                    context,
                    asset,
                    managed,
                    profileId,
                    cancellationToken),
            AkeneoAssetDestinationType.ProductVideo =>
                await ApplyVideoAsync(
                    context,
                    asset,
                    managed,
                    profileId),
            _ => AssetApplyResult.Incomplete
        };
    }

    private async Task<AssetApplyResult> ApplyPictureAsync(
        AkeneoProductSyncContext context,
        AkeneoResolvedAsset asset,
        AkeneoManagedAsset managed,
        int profileId,
        CancellationToken cancellationToken)
    {
        if ((AkeneoAssetStorageMode)asset.Mapping.StorageModeId !=
            AkeneoAssetStorageMode.ImportIntoNopCommerce)
        {
            context.Result.AddWarning(
                $"Asset mapping '{asset.Mapping.Name}' targets Product Picture but is configured as an external reference.");
            return AssetApplyResult.Incomplete;
        }

        ProductPicture productPicture = null;
        Picture picture = null;
        if (managed?.NopProductPictureId > 0)
            productPicture = await productService.GetProductPictureByIdAsync(managed.NopProductPictureId.Value);
        if (managed?.NopPictureId > 0)
            picture = await pictureService.GetPictureByIdAsync(managed.NopPictureId.Value);

        var changed = false;
        var binaryChanged = managed == null ||
            !string.Equals(
                managed.SourceFingerprint,
                asset.SourceFingerprint,
                StringComparison.OrdinalIgnoreCase) ||
            picture == null;

        if (binaryChanged)
        {
            var file = await DownloadAsync(context, asset, cancellationToken);
            var mimeType = file.ContentType ?? asset.MimeType;
            if (string.IsNullOrWhiteSpace(mimeType) ||
                !mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                context.Result.AddWarning(
                    $"Asset '{asset.AssetCode ?? asset.MediaFileCode}' is not an image and was not imported as a product picture.");
                return AssetApplyResult.Incomplete;
            }

            var seoName = await ResolveSeoFilenameAsync(
                asset,
                file.FileName);
            picture = picture == null
                ? await pictureService.InsertPictureAsync(
                    file.Bytes,
                    mimeType,
                    seoName,
                    asset.AltText,
                    asset.TitleText)
                : await pictureService.UpdatePictureAsync(
                    picture.Id,
                    file.Bytes,
                    mimeType,
                    seoName,
                    asset.AltText,
                    asset.TitleText);
            changed = true;
        }
        else
        {
            var seoName = await ResolveSeoFilenameAsync(asset);
            var metadataChanged =
                !string.Equals(picture.SeoFilename, seoName, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(picture.AltAttribute, asset.AltText, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(picture.TitleAttribute, asset.TitleText, StringComparison.OrdinalIgnoreCase);

            if (metadataChanged)
            {
                picture.SeoFilename = seoName;
                picture.AltAttribute = asset.AltText;
                picture.TitleAttribute = asset.TitleText;
                await pictureService.UpdatePictureAsync(picture);
                changed = true;
            }
        }

        if (picture == null)
            return AssetApplyResult.Incomplete;

        if (productPicture == null)
        {
            productPicture = new ProductPicture
            {
                ProductId = context.Product.Id,
                PictureId = picture.Id,
                DisplayOrder = asset.DisplayOrder
            };
            await productService.InsertProductPictureAsync(productPicture);
            changed = true;
        }
        else if (productPicture.PictureId != picture.Id ||
                 productPicture.DisplayOrder != asset.DisplayOrder)
        {
            productPicture.PictureId = picture.Id;
            productPicture.DisplayOrder = asset.DisplayOrder;
            await productService.UpdateProductPictureAsync(productPicture);
            changed = true;
        }

        await UpsertManagedAsync(
            context,
            asset,
            managed,
            profileId,
            nopPictureId: picture.Id,
            nopProductPictureId: productPicture.Id);

        if (changed)
        {
            context.Result.AddMessage(
                $"Synchronized product picture '{asset.OriginalFileName ?? asset.AssetCode ?? asset.MediaFileCode}'.");
        }

        return new AssetApplyResult(true, changed);
    }

    private async Task<AssetApplyResult> ApplyVideoAsync(
        AkeneoProductSyncContext context,
        AkeneoResolvedAsset asset,
        AkeneoManagedAsset managed,
        int profileId)
    {
        if (string.IsNullOrWhiteSpace(asset.ExternalUrl))
        {
            context.Result.AddWarning(
                $"Asset '{asset.AssetCode ?? asset.MediaFileCode}' has no external video URL.");
            return AssetApplyResult.Incomplete;
        }

        Video video = null;
        ProductVideo productVideo = null;
        if (managed?.NopVideoId > 0)
            video = await videoService.GetVideoByIdAsync(managed.NopVideoId.Value);
        if (managed?.NopProductVideoId > 0)
            productVideo = await productService.GetProductVideoByIdAsync(managed.NopProductVideoId.Value);

        var changed = false;
        if (video == null)
        {
            video = new Video { VideoUrl = asset.ExternalUrl };
            await videoService.InsertVideoAsync(video);
            changed = true;
        }
        else if (!string.Equals(video.VideoUrl, asset.ExternalUrl, StringComparison.OrdinalIgnoreCase))
        {
            video.VideoUrl = asset.ExternalUrl;
            await videoService.UpdateVideoAsync(video);
            changed = true;
        }

        if (productVideo == null)
        {
            productVideo = new ProductVideo
            {
                ProductId = context.Product.Id,
                VideoId = video.Id,
                DisplayOrder = asset.DisplayOrder
            };
            await productService.InsertProductVideoAsync(productVideo);
            changed = true;
        }
        else if (productVideo.VideoId != video.Id ||
                 productVideo.DisplayOrder != asset.DisplayOrder)
        {
            productVideo.VideoId = video.Id;
            productVideo.DisplayOrder = asset.DisplayOrder;
            await productService.UpdateProductVideoAsync(productVideo);
            changed = true;
        }

        await UpsertManagedAsync(
            context,
            asset,
            managed,
            profileId,
            nopVideoId: video.Id,
            nopProductVideoId: productVideo.Id);

        if (changed)
            context.Result.AddMessage($"Synchronized product video '{asset.ExternalUrl}'.");
        return new AssetApplyResult(true, changed);
    }

    private async Task<AssetApplyResult> ApplyCustomPropertyAsync(
        AkeneoProductSyncContext context,
        AkeneoAssetMapping mapping,
        IReadOnlyList<AkeneoResolvedAsset> desired,
        IList<AkeneoManagedAsset> existing)
    {
        var key = GetCustomPropertyKey(mapping);
        var current = await genericAttributeService.GetAttributeAsync<string>(
            context.Product,
            key);

        if (!TryBuildCustomPropertyPayload(
                current,
                desired,
                context.Request.AssetSyncMode,
                out var desiredJson,
                out var payloadCount,
                out var warning))
        {
            context.Result.AddWarning(
                $"Managed asset property '{key}' was preserved: {warning}");
            return AssetApplyResult.Incomplete;
        }

        var changed = !string.Equals(
            current,
            desiredJson,
            StringComparison.OrdinalIgnoreCase);

        if (changed)
        {
            await genericAttributeService.SaveAttributeAsync(
                context.Product,
                key,
                desiredJson);
            context.Result.AddMessage(
                desiredJson == null
                    ? $"Cleared managed asset property '{key}'."
                    : $"Synchronized {payloadCount} asset reference(s) to '{key}'.");
        }

        var profileId = context.Request.SyncProfileId.GetValueOrDefault();
        foreach (var asset in desired)
        {
            var managed = existing.FirstOrDefault(item =>
                string.Equals(
                    item.SourceIdentityHash,
                    asset.SourceIdentityHash,
                    StringComparison.OrdinalIgnoreCase));
            await UpsertManagedAsync(
                context,
                asset,
                managed,
                profileId,
                destinationKey: key);
        }

        return new AssetApplyResult(true, changed);
    }

    private static string GetPayloadIdentity(
        ManagedAssetReferencePayload payload)
    {
        var naturalIdentity = string.Join("|", new[]
        {
            payload.AssetCode,
            payload.MediaFileCode,
            payload.Url
        }.Select(value => value?.Trim() ?? string.Empty));

        return naturalIdentity.Any(character => character != '|')
            ? naturalIdentity
            : payload.SourceIdentity?.Trim() ?? string.Empty;
    }

    private async Task<string> ResolveSeoFilenameAsync(
        AkeneoResolvedAsset asset,
        string downloadedFileName = null)
    {
        var sourceName = asset.SeoFilename ??
            downloadedFileName ??
            asset.OriginalFileName ??
            asset.AssetCode ??
            asset.MediaFileCode ??
            "akeneo-asset";

        return await pictureService.GetPictureSeNameAsync(sourceName);
    }

    private async Task<AkeneoBinaryFile> DownloadAsync(
        AkeneoProductSyncContext context,
        AkeneoResolvedAsset asset,
        CancellationToken cancellationToken)
    {
        var key = BuildBinaryCacheKey(asset);
        if (key != null && context.PreloadedAssetBinaries.TryGetValue(key, out var preloaded))
            return preloaded;

        return await DownloadFromSourceAsync(asset, cancellationToken); // fallback
    }

    private async Task<AkeneoBinaryFile> DownloadFromSourceAsync(
        AkeneoResolvedAsset asset,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(asset.ExternalUrl))
        {
            return await externalAssetDownloader.DownloadAsync(
                asset.ExternalUrl,
                cancellationToken);
        }

        if ((AkeneoAssetSourceType)asset.Mapping.SourceTypeId ==
            AkeneoAssetSourceType.ProductMediaAttribute)
        {
            return await apiClient.DownloadProductMediaFileAsync(
                asset.MediaFileCode,
                cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(asset.DownloadUrl))
        {
            return await apiClient.DownloadAssetMediaFileByUrlAsync(
                asset.DownloadUrl,
                cancellationToken);
        }

        return await apiClient.DownloadAssetMediaFileAsync(
            asset.MediaFileCode,
            cancellationToken);
    }

    private async Task UpsertManagedAsync(
        AkeneoProductSyncContext context,
        AkeneoResolvedAsset asset,
        AkeneoManagedAsset managed,
        int profileId,
        int? nopPictureId = null,
        int? nopProductPictureId = null,
        int? nopVideoId = null,
        int? nopProductVideoId = null,
        string destinationKey = null)
    {
        var now = DateTime.UtcNow;
        managed ??= new AkeneoManagedAsset
        {
            SyncProfileId = profileId,
            AssetMappingId = asset.Mapping.Id,
            AssetMappingKey = asset.Mapping.MappingKey,
            NopProductId = context.Product.Id,
            SourceIdentityHash = asset.SourceIdentityHash,
            CreatedOnUtc = now
        };

        managed.SyncProfileId = profileId;
        managed.AssetMappingId = asset.Mapping.Id;
        managed.AssetMappingKey = asset.Mapping.MappingKey;
        managed.DestinationTypeId = asset.Mapping.DestinationTypeId;
        managed.NopPictureId = nopPictureId ?? managed.NopPictureId;
        managed.NopProductPictureId = nopProductPictureId ?? managed.NopProductPictureId;
        managed.NopVideoId = nopVideoId ?? managed.NopVideoId;
        managed.NopProductVideoId = nopProductVideoId ?? managed.NopProductVideoId;
        managed.SourceFingerprint = asset.SourceFingerprint;
        managed.SourceAttributeCode = asset.SourceAttributeCode;
        managed.AssetFamilyCode = asset.AssetFamilyCode;
        managed.AssetCode = asset.AssetCode;
        managed.MediaFileCode = asset.MediaFileCode;
        managed.SourceUrl = asset.ExternalUrl;
        managed.DestinationKey = destinationKey ?? managed.DestinationKey;
        managed.DisplayOrder = asset.DisplayOrder;
        managed.LastSeenRunRecordId = context.Request.SyncRunRecordId;
        managed.UpdatedOnUtc = now;

        if (managed.Id == 0)
            await managedAssetService.InsertAsync(managed);
        else
            await managedAssetService.UpdateAsync(managed);
    }

    private async Task<bool> DeleteManagedDestinationAsync(AkeneoManagedAsset managed)
    {
        var changed = false;
        if (managed.NopProductPictureId > 0)
        {
            var relation = await productService.GetProductPictureByIdAsync(managed.NopProductPictureId.Value);
            if (relation != null)
            {
                await productService.DeleteProductPictureAsync(relation);
                changed = true;
            }
        }
        if (managed.NopPictureId > 0)
        {
            var picture = await pictureService.GetPictureByIdAsync(managed.NopPictureId.Value);
            if (picture != null)
            {
                await pictureService.DeletePictureAsync(picture);
                changed = true;
            }
        }
        if (managed.NopProductVideoId > 0)
        {
            var relation = await productService.GetProductVideoByIdAsync(managed.NopProductVideoId.Value);
            if (relation != null)
            {
                await productService.DeleteProductVideoAsync(relation);
                changed = true;
            }
        }
        if (managed.NopVideoId > 0)
        {
            var video = await videoService.GetVideoByIdAsync(managed.NopVideoId.Value);
            if (video != null)
            {
                await videoService.DeleteVideoAsync(video);
                changed = true;
            }
        }
        return changed;
    }

    private async Task RemoveOrphanedMappingsAsync(
        AkeneoProductSyncContext context,
        IEnumerable<string> activeMappingKeys,
        CancellationToken cancellationToken)
    {
        var active = activeMappingKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var managedAssets = await managedAssetService.GetByProductAsync(
            context.Product.Id);
        var clearedCustomKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var changed = false;

        foreach (var orphan in managedAssets.Where(item =>
                     !active.Contains(item.AssetMappingKey)).ToList())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if ((AkeneoAssetDestinationType)orphan.DestinationTypeId ==
                    AkeneoAssetDestinationType.CustomProperty &&
                !string.IsNullOrWhiteSpace(orphan.DestinationKey) &&
                clearedCustomKeys.Add(orphan.DestinationKey))
            {
                await genericAttributeService.SaveAttributeAsync<string>(
                    context.Product,
                    orphan.DestinationKey,
                    null);
                changed = true;
            }
            else
            {
                changed |= await DeleteManagedDestinationAsync(orphan);
            }

            await managedAssetService.DeleteAsync(orphan);
        }

        if (changed)
        {
            context.MarkChanged();
            context.Result.AddMessage("Removed stale assets from deleted or disabled asset mappings.");
        }
    }

    private async Task FinalizeReplaceAllAsync(
        AkeneoProductSyncContext context,
        IReadOnlyCollection<AkeneoAssetMapping> activeMappings,
        IReadOnlyCollection<ResolvedAssetMapping> resolvedMappings,
        CancellationToken cancellationToken)
    {
        var activeMappingKeys = activeMappings
            .Select(mapping => mapping.MappingKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var desiredIdentities = resolvedMappings.ToDictionary(
            item => item.Mapping.MappingKey,
            item => item.Assets
                .Select(asset => asset.SourceIdentityHash)
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        var managedAssets = await managedAssetService.GetByProductAsync(
            context.Product.Id);
        var desiredManagedAssets = managedAssets
            .Where(item =>
                activeMappingKeys.Contains(item.AssetMappingKey) &&
                desiredIdentities.TryGetValue(
                    item.AssetMappingKey,
                    out var identities) &&
                identities.Contains(item.SourceIdentityHash))
            .ToList();
        var desiredManagedIds = desiredManagedAssets
            .Select(item => item.Id)
            .ToHashSet();
        var destinationTypes = activeMappings
            .Select(mapping =>
                (AkeneoAssetDestinationType)mapping.DestinationTypeId)
            .ToHashSet();
        var changed = false;

        // Authoritative collection cleanup happens only after every desired
        // asset has been created or updated successfully. The keep sets are
        // based on plugin ownership records created during the apply phase.
        if (destinationTypes.Contains(
                AkeneoAssetDestinationType.ProductPicture))
        {
            var keepRelations = desiredManagedAssets
                .Where(item =>
                    item.DestinationTypeId ==
                        (int)AkeneoAssetDestinationType.ProductPicture &&
                    item.NopProductPictureId > 0)
                .Select(item => item.NopProductPictureId.Value)
                .ToHashSet();

            foreach (var relation in await productService
                         .GetProductPicturesByProductIdAsync(
                             context.Product.Id))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (keepRelations.Contains(relation.Id))
                    continue;

                var picture = await pictureService.GetPictureByIdAsync(
                    relation.PictureId);
                await productService.DeleteProductPictureAsync(relation);
                if (picture != null)
                    await pictureService.DeletePictureAsync(picture);
                changed = true;
            }
        }

        if (destinationTypes.Contains(
                AkeneoAssetDestinationType.ProductVideo))
        {
            var keepRelations = desiredManagedAssets
                .Where(item =>
                    item.DestinationTypeId ==
                        (int)AkeneoAssetDestinationType.ProductVideo &&
                    item.NopProductVideoId > 0)
                .Select(item => item.NopProductVideoId.Value)
                .ToHashSet();

            foreach (var relation in await productService
                         .GetProductVideosByProductIdAsync(
                             context.Product.Id))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (keepRelations.Contains(relation.Id))
                    continue;

                var video = await videoService.GetVideoByIdAsync(
                    relation.VideoId);
                await productService.DeleteProductVideoAsync(relation);
                if (video != null)
                    await videoService.DeleteVideoAsync(video);
                changed = true;
            }
        }

        var activeCustomKeys = activeMappings
            .Where(mapping =>
                (AkeneoAssetDestinationType)mapping.DestinationTypeId ==
                    AkeneoAssetDestinationType.CustomProperty)
            .ToDictionary(
                mapping => mapping.MappingKey,
                GetCustomPropertyKey,
                StringComparer.OrdinalIgnoreCase);
        var clearedCustomKeys = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var stale in managedAssets
                     .Where(item => !desiredManagedIds.Contains(item.Id))
                     .ToList())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if ((AkeneoAssetDestinationType)stale.DestinationTypeId ==
                    AkeneoAssetDestinationType.CustomProperty)
            {
                var isStillActiveDestination =
                    activeCustomKeys.TryGetValue(
                        stale.AssetMappingKey,
                        out var activeKey) &&
                    string.Equals(
                        activeKey,
                        stale.DestinationKey,
                        StringComparison.OrdinalIgnoreCase);

                // Active custom-property mappings already rewrote their payload
                // without stale items. Only clear keys that no active mapping owns.
                if (!isStillActiveDestination &&
                    !string.IsNullOrWhiteSpace(stale.DestinationKey) &&
                    clearedCustomKeys.Add(stale.DestinationKey))
                {
                    await genericAttributeService.SaveAttributeAsync<string>(
                        context.Product,
                        stale.DestinationKey,
                        null);
                    changed = true;
                }
            }
            else
            {
                // Collection cleanup above may already have removed this
                // destination. The helper is intentionally idempotent.
                changed |= await DeleteManagedDestinationAsync(stale);
            }

            await managedAssetService.DeleteAsync(stale);
        }

        if (changed)
        {
            context.MarkChanged();
            context.Result.AddMessage(
                "Completed authoritative asset replacement after all desired assets were synchronized successfully.");
        }
    }

    private sealed class ManagedAssetReferencePayload
    {
        public ManagedAssetReferencePayload()
        {
        }

        [JsonPropertyName("sourceIdentity")]
        public string SourceIdentity { get; init; }

        [JsonPropertyName("assetCode")]
        public string AssetCode { get; init; }

        [JsonPropertyName("mediaFileCode")]
        public string MediaFileCode { get; init; }

        [JsonPropertyName("url")]
        public string Url { get; init; }

        [JsonPropertyName("mimeType")]
        public string MimeType { get; init; }

        [JsonPropertyName("fileName")]
        public string FileName { get; init; }

        [JsonPropertyName("displayOrder")]
        public int DisplayOrder { get; init; }

        [JsonPropertyName("altText")]
        public string AltText { get; init; }

        [JsonPropertyName("titleText")]
        public string TitleText { get; init; }

        public static ManagedAssetReferencePayload From(
            AkeneoResolvedAsset asset) => new()
        {
            SourceIdentity = asset.SourceIdentityHash,
            AssetCode = asset.AssetCode,
            MediaFileCode = asset.MediaFileCode,
            Url = asset.ExternalUrl,
            MimeType = asset.MimeType,
            FileName = asset.OriginalFileName,
            DisplayOrder = asset.DisplayOrder,
            AltText = asset.AltText,
            TitleText = asset.TitleText
        };
    }

    private readonly record struct AssetApplyResult(
        bool Complete,
        bool Changed)
    {
        public static AssetApplyResult CompleteUnchanged => new(true, false);
        public static AssetApplyResult Incomplete => new(false, false);
    }

    private sealed record ResolvedAssetMapping(
        AkeneoAssetMapping Mapping,
        IReadOnlyList<AkeneoResolvedAsset> Assets);

    private readonly record struct AssetDesiredState(
        List<AkeneoAssetMapping> ActiveMappings,
        List<ResolvedAssetMapping> ResolvedMappings,
        bool AuthoritativeResolutionFailed);
}
