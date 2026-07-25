using System.Text.Json;
using System.Text.Json.Serialization;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Assets;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Media;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Media;

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
    : IAkeneoProductSectionSynchronizer
{
    public int Order => 550;

    public async Task SynchronizeAsync(
        AkeneoProductSyncContext context,
        CancellationToken cancellationToken = default)
    {
        if (context.Product == null ||
            context.Request.AssetSyncMode == AkeneoCollectionSyncMode.Disabled)
        {
            return;
        }

        var mappings = await assetMappingService.GetEffectiveMappingsAsync(
            context.MappingFamilyCode);
        var currentScope = AkeneoAttributeMappingScopeHelper.NormalizeCurrentScope(
            context.MappingEntityScope,
            context.Source,
            context.SourceEntityType);

        var activeMappings = mappings.Where(mapping =>
                mapping.Enabled &&
                AkeneoAttributeMappingScopeHelper
                    .NormalizeConfiguredScope(mapping.EntityScopeId)
                    .HasFlag(currentScope))
            .ToList();

        var resolvedMappings = new List<ResolvedAssetMapping>();
        var authoritativeResolutionFailed = false;

        // Resolve the complete desired state before making destructive changes.
        // This is especially important for ReplaceAll: a temporary Akeneo error,
        // missing locale/channel match, or invalid external asset must never clear
        // the product gallery before the replacement set is known to be safe.
        foreach (var mapping in activeMappings)
        {
            try
            {
                var resolution = await assetResolver.ResolveAsync(
                    context,
                    mapping,
                    cancellationToken);

                if (!string.IsNullOrWhiteSpace(resolution.Warning))
                    context.Result.AddWarning(resolution.Warning);

                if (!resolution.CanReconcile)
                {
                    authoritativeResolutionFailed = true;
                    continue;
                }

                resolvedMappings.Add(new ResolvedAssetMapping(
                    mapping,
                    resolution.Assets));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                authoritativeResolutionFailed = true;
                context.Result.AddWarning(
                    $"Asset mapping '{mapping.Name}' could not be resolved: {ex.Message}");
            }
        }

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
            var file = await DownloadAsync(asset, cancellationToken);
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

        if (desired.Count == 0 &&
            context.Request.AssetSyncMode == AkeneoCollectionSyncMode.Merge)
        {
            return AssetApplyResult.CompleteUnchanged;
        }

        var payloadByIdentity = new Dictionary<string, ManagedAssetReferencePayload>(
            StringComparer.OrdinalIgnoreCase);

        if (context.Request.AssetSyncMode == AkeneoCollectionSyncMode.Merge &&
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
                context.Result.AddWarning(
                    $"Managed asset property '{key}' contains invalid JSON and was preserved: {ex.Message}");
                return AssetApplyResult.Incomplete;
            }
        }

        foreach (var asset in desired)
        {
            var item = ManagedAssetReferencePayload.From(asset);
            payloadByIdentity[GetPayloadIdentity(item)] = item;
        }

        var payload = payloadByIdentity.Values
            .OrderBy(item => item.DisplayOrder)
            .ThenBy(item => item.AssetCode ?? item.MediaFileCode ?? item.Url,
                StringComparer.OrdinalIgnoreCase)
            .ToList();
        var desiredJson = payload.Count == 0
            ? null
            : JsonSerializer.Serialize(payload);

        var changed = !string.Equals(current, desiredJson, StringComparison.OrdinalIgnoreCase);
        if (changed)
        {
            await genericAttributeService.SaveAttributeAsync(
                context.Product,
                key,
                desiredJson);
            context.Result.AddMessage(
                desiredJson == null
                    ? $"Cleared managed asset property '{key}'."
                    : $"Synchronized {payload.Count} asset reference(s) to '{key}'.");
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

}
