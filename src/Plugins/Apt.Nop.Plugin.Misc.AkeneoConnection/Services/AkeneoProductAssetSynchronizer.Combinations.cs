using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Assets;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Media;
using static Apt.Nop.Plugin.Misc.AkeneoConnection.Services.DryRun.AkeneoDryRunPlanHelper;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

// Variants represented as attribute combinations. A "Variant products" asset
// mapping gives such a leaf its pictures: each image goes into the parent's
// gallery and is linked to the combination (ProductAttributeCombinationPicture),
// in display order. Each combination has its own managed-asset rows on the
// parent, so combinations sharing an image share one picture, which is deleted
// only when nothing uses it any more.
public sealed partial class AkeneoProductAssetSynchronizer
{
    /// <summary>
    /// Combination pictures sort after the parent's own gallery pictures.
    /// </summary>
    internal const int CombinationPictureDisplayOrderOffset = 1000;

    public async Task<bool> SynchronizeCombinationAsync(
        AkeneoProductSyncContext leafContext,
        Product parentProduct,
        int combinationId,
        CancellationToken cancellationToken = default)
    {
        if (leafContext == null || parentProduct is not { Id: > 0 } || combinationId <= 0)
            return false;

        var syncMode = leafContext.Request.AssetSyncMode;
        if (syncMode == AkeneoCollectionSyncMode.Disabled)
            return false;

        var combination = await productAttributeService.GetProductAttributeCombinationByIdAsync(combinationId);
        if (combination == null || combination.ProductId != parentProduct.Id)
            return false;

        var changed = await ReleaseDeletedCombinationsAsync(parentProduct.Id, cancellationToken);

        var desired = await ResolveDesiredAsync(leafContext, leafContext.Result.AddWarning, cancellationToken);
        if (desired.AuthoritativeResolutionFailed)
        {
            leafContext.Result.AddWarning(
                $"The pictures of combination '{combination.Sku}' were left unchanged because an asset mapping could not be resolved safely.");
            return changed;
        }

        var assets = SelectCombinationAssets(desired.ResolvedMappings, leafContext.Result.AddWarning);
        var rows = await managedAssetService.GetByCombinationAsync(combinationId);

        var kept = new List<AkeneoManagedAsset>();
        var complete = true;
        foreach (var asset in assets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var outcome = await ApplyCombinationAssetAsync(
                leafContext, parentProduct, combinationId, asset, rows, cancellationToken);
            changed |= outcome.Changed;
            if (outcome.Row == null)
                complete = false;
            else
                kept.Add(outcome.Row);
        }

        var links = await productAttributeService.GetProductAttributeCombinationPicturesAsync(combinationId);
        foreach (var pictureId in kept.Select(row => row.NopPictureId.Value).Distinct())
        {
            if (links.Any(link => link.PictureId == pictureId))
                continue;

            await productAttributeService.InsertProductAttributeCombinationPictureAsync(
                new ProductAttributeCombinationPicture
                {
                    ProductAttributeCombinationId = combinationId,
                    PictureId = pictureId
                });
            changed = true;
        }

        // A failed image keeps everything else as it is, as on products.
        if (!complete)
            return changed;

        var keptIds = kept.Select(row => row.Id).ToHashSet();
        var stale = rows.Where(row => !keptIds.Contains(row.Id)).ToList();
        var removable = CombinationLinksToRemove(syncMode, links, kept, stale, desired.ActiveMappings);
        foreach (var link in removable)
        {
            await productAttributeService.DeleteProductAttributeCombinationPictureAsync(link);
            changed = true;
        }

        if (removable.Count > 0)
            leafContext.Result.AddMessage($"Removed {removable.Count} picture(s) from combination '{combination.Sku}'.");

        // Merge keeps earlier images (and their rows); the replace modes drop them.
        if (syncMode != AkeneoCollectionSyncMode.Merge)
        {
            foreach (var row in stale)
            {
                cancellationToken.ThrowIfCancellationRequested();
                changed |= await ReleaseRowAsync(row);
            }
        }

        return changed;
    }

    public async Task ReleaseCombinationAsync(
        int combinationId,
        CancellationToken cancellationToken = default)
    {
        if (combinationId <= 0)
            return;

        foreach (var row in await managedAssetService.GetByCombinationAsync(combinationId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ReleaseRowAsync(row);
        }
    }

    /// <summary>
    /// A leaf whose profile state binds it to an attribute combination.
    /// </summary>
    internal static bool IsCombinationLeaf(AkeneoProductSyncContext context) =>
        context?.SourceEntityType == AkeneoEntityType.Product &&
        context.ExistingSyncState is { } state &&
        state.DestinationKindId == (int)AkeneoProductDestinationKind.ProductAttributeCombination &&
        state.NopProductAttributeCombinationId is > 0;

    /// <summary>
    /// Links the sync removes when it has applied every image: Replace managed
    /// removes links to pictures of images the combination no longer has;
    /// Replace all removes every other link, but only while a picture mapping
    /// applies (as product-level Replace all leaves galleries alone without
    /// one); Merge removes none.
    /// </summary>
    internal static IList<ProductAttributeCombinationPicture> CombinationLinksToRemove(
        AkeneoCollectionSyncMode syncMode,
        IEnumerable<ProductAttributeCombinationPicture> links,
        IEnumerable<AkeneoManagedAsset> keptRows,
        IEnumerable<AkeneoManagedAsset> staleRows,
        IEnumerable<AkeneoAssetMapping> activeMappings)
    {
        var keep = keptRows.Where(row => row.NopPictureId > 0).Select(row => row.NopPictureId.Value).ToHashSet();
        var stale = staleRows.Where(row => row.NopPictureId > 0).Select(row => row.NopPictureId.Value).ToHashSet();
        var replaceAll = syncMode == AkeneoCollectionSyncMode.ReplaceAll &&
            activeMappings.Any(mapping =>
                (AkeneoAssetDestinationType)mapping.DestinationTypeId == AkeneoAssetDestinationType.ProductPicture);

        return syncMode switch
        {
            AkeneoCollectionSyncMode.ReplaceManaged or AkeneoCollectionSyncMode.ReplaceAll =>
                links.Where(link => !keep.Contains(link.PictureId) && (replaceAll || stale.Contains(link.PictureId))).ToList(),
            _ => new List<ProductAttributeCombinationPicture>()
        };
    }

    /// <summary>
    /// Every importable picture across the resolved mappings, in asset display
    /// order, then mapping display order and id.
    /// </summary>
    private static List<AkeneoResolvedAsset> SelectCombinationAssets(
        IEnumerable<ResolvedAssetMapping> resolvedMappings,
        Action<string> warningSink)
    {
        var candidates = new List<AkeneoResolvedAsset>();
        foreach (var resolved in resolvedMappings)
        {
            if ((AkeneoAssetDestinationType)resolved.Mapping.DestinationTypeId != AkeneoAssetDestinationType.ProductPicture)
                continue; // a combination has no videos or custom properties

            if ((AkeneoAssetStorageMode)resolved.Mapping.StorageModeId != AkeneoAssetStorageMode.ImportIntoNopCommerce)
            {
                if (resolved.Assets.Count > 0)
                {
                    warningSink?.Invoke(
                        $"Asset mapping '{resolved.Mapping.Name}' targets Product Picture but is configured as an external reference, so it was not used for the combination pictures.");
                }

                continue;
            }

            candidates.AddRange(resolved.Assets);
        }

        return candidates
            .OrderBy(asset => asset.DisplayOrder)
            .ThenBy(asset => asset.Mapping.DisplayOrder)
            .ThenBy(asset => asset.Mapping.Id)
            .ToList();
    }

    private async Task<(AkeneoManagedAsset Row, bool Changed)> ApplyCombinationAssetAsync(
        AkeneoProductSyncContext context,
        Product parentProduct,
        int combinationId,
        AkeneoResolvedAsset asset,
        IList<AkeneoManagedAsset> combinationRows,
        CancellationToken cancellationToken)
    {
        var row = combinationRows.FirstOrDefault(item =>
            string.Equals(item.AssetMappingKey, asset.Mapping.MappingKey, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.SourceIdentityHash, asset.SourceIdentityHash, StringComparison.OrdinalIgnoreCase));

        var picture = row?.NopPictureId > 0 ? await pictureService.GetPictureByIdAsync(row.NopPictureId.Value) : null;
        var relation = row?.NopProductPictureId > 0 ? await productService.GetProductPictureByIdAsync(row.NopProductPictureId.Value) : null;
        var previousPictureId = row?.NopPictureId;
        var changed = false;

        var current = picture != null && relation != null &&
            string.Equals(row.SourceFingerprint, asset.SourceFingerprint, StringComparison.OrdinalIgnoreCase);

        if (current)
        {
            // Shared pictures keep the metadata of whoever wrote them, so two
            // combinations with different alt text don't rewrite it every run.
            if (!await IsPictureUsedByOtherRowsAsync(row))
                changed |= await UpdatePictureMetadataAsync(picture, asset);
        }
        else
        {
            var allRows = await managedAssetService.GetAllByProductAsync(parentProduct.Id);
            var shared = await FindSharedPictureAsync(allRows, row, asset);

            if (shared != null)
            {
                (picture, relation) = shared.Value;
            }
            else
            {
                var file = await DownloadAsync(context, asset, cancellationToken);
                var mimeType = file.ContentType ?? asset.MimeType;
                if (string.IsNullOrWhiteSpace(mimeType) ||
                    !mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                {
                    context.Result.AddWarning(
                        $"Asset '{asset.AssetCode ?? asset.MediaFileCode}' is not an image and was not used as a combination picture.");
                    return (null, false);
                }

                var seoName = await ResolveSeoFilenameAsync(asset, file.FileName);
                var identical = await FindIdenticalGalleryPictureAsync(parentProduct.Id, file.Bytes);

                if (identical != null)
                {
                    (relation, picture) = identical.Value;
                    context.Result.AddMessage(
                        $"Product picture #{picture.Id} already has the image of Akeneo asset '{asset.AssetCode ?? asset.MediaFileCode}', so the combination uses it.");
                }
                else if (picture != null && OnlyUsedBy(allRows, picture.Id, asset.SourceIdentityHash))
                {
                    // Every row using the picture is this image, so the new
                    // version replaces it in place for all of them.
                    picture = await pictureService.UpdatePictureAsync(
                        picture.Id, file.Bytes, mimeType, seoName, asset.AltText, asset.TitleText);
                }
                else
                {
                    picture = await pictureService.InsertPictureAsync(
                        file.Bytes, mimeType, seoName, asset.AltText, asset.TitleText);
                    relation = null;
                }

                changed = true;
            }
        }

        if (picture == null)
            return (null, changed);

        if (relation == null || relation.PictureId != picture.Id || relation.ProductId != parentProduct.Id)
        {
            relation = (await productService.GetProductPicturesByProductIdAsync(parentProduct.Id))
                .FirstOrDefault(item => item.PictureId == picture.Id);
        }

        if (relation == null)
        {
            relation = new ProductPicture
            {
                ProductId = parentProduct.Id,
                PictureId = picture.Id,
                DisplayOrder = CombinationPictureDisplayOrderOffset + asset.DisplayOrder
            };
            await productService.InsertProductPictureAsync(relation);
            changed = true;
        }

        var now = DateTime.UtcNow;
        row ??= new AkeneoManagedAsset
        {
            AssetMappingKey = asset.Mapping.MappingKey,
            NopProductId = parentProduct.Id,
            NopProductAttributeCombinationId = combinationId,
            SourceIdentityHash = asset.SourceIdentityHash,
            CreatedOnUtc = now
        };

        row.SyncProfileId = context.Request.SyncProfileId.GetValueOrDefault();
        row.AssetMappingId = asset.Mapping.Id;
        row.DestinationTypeId = (int)AkeneoAssetDestinationType.ProductPicture;
        row.NopPictureId = picture.Id;
        row.NopProductPictureId = relation.Id;
        row.SourceFingerprint = asset.SourceFingerprint;
        row.SourceAttributeCode = asset.SourceAttributeCode;
        row.AssetFamilyCode = asset.AssetFamilyCode;
        row.AssetCode = asset.AssetCode;
        row.MediaFileCode = asset.MediaFileCode;
        row.SourceUrl = asset.ExternalUrl;
        row.DisplayOrder = asset.DisplayOrder;
        row.LastSeenRunRecordId = context.Request.SyncRunRecordId;
        row.UpdatedOnUtc = now;

        if (row.Id == 0)
            await managedAssetService.InsertAsync(row);
        else
            await managedAssetService.UpdateAsync(row);

        // The row moved to another picture: unlink the old one from this
        // combination, and delete it if nothing else uses it.
        if (previousPictureId > 0 && previousPictureId != picture.Id)
        {
            foreach (var link in (await productAttributeService.GetProductAttributeCombinationPicturesAsync(combinationId))
                         .Where(link => link.PictureId == previousPictureId.Value)
                         .ToList())
            {
                await productAttributeService.DeleteProductAttributeCombinationPictureAsync(link);
            }

            changed |= await DeletePictureIfUnusedAsync(previousPictureId.Value, parentProduct.Id, combinationId);
        }

        if (changed)
        {
            context.Result.AddMessage(
                $"Synchronized combination picture '{asset.OriginalFileName ?? asset.AssetCode ?? asset.MediaFileCode}'.");
        }

        return (row, changed);
    }

    /// <summary>
    /// A picture another row on the parent already holds for this exact image
    /// version, so no download is needed.
    /// </summary>
    private async Task<(Picture Picture, ProductPicture Relation)?> FindSharedPictureAsync(
        IEnumerable<AkeneoManagedAsset> allRows,
        AkeneoManagedAsset self,
        AkeneoResolvedAsset asset)
    {
        foreach (var other in allRows.Where(item =>
                     item.Id != self?.Id &&
                     item.NopPictureId > 0 &&
                     item.DestinationTypeId == (int)AkeneoAssetDestinationType.ProductPicture &&
                     string.Equals(item.SourceIdentityHash, asset.SourceIdentityHash, StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(item.SourceFingerprint, asset.SourceFingerprint, StringComparison.OrdinalIgnoreCase)))
        {
            var picture = await pictureService.GetPictureByIdAsync(other.NopPictureId.Value);
            if (picture == null)
                continue;

            var relation = other.NopProductPictureId > 0
                ? await productService.GetProductPictureByIdAsync(other.NopProductPictureId.Value)
                : null;
            return (picture, relation);
        }

        return null;
    }

    /// <summary>
    /// Any gallery picture of the product with exactly these bytes, claimed or
    /// not: a combination may share a picture the product itself uses.
    /// </summary>
    private async Task<(ProductPicture Relation, Picture Picture)?> FindIdenticalGalleryPictureAsync(
        int productId,
        byte[] bytes)
    {
        if (bytes is not { Length: > 0 })
            return null;

        foreach (var relation in await productService.GetProductPicturesByProductIdAsync(productId))
        {
            var existing = await pictureService.GetPictureByIdAsync(relation.PictureId);
            if (existing == null)
                continue;

            var existingBytes = await pictureService.LoadPictureBinaryAsync(existing);
            if (existingBytes != null && existingBytes.AsSpan().SequenceEqual(bytes))
                return (relation, existing);
        }

        return null;
    }

    private static bool OnlyUsedBy(
        IEnumerable<AkeneoManagedAsset> rows,
        int pictureId,
        string sourceIdentityHash) =>
        rows.Where(item => item.NopPictureId == pictureId)
            .All(item => string.Equals(item.SourceIdentityHash, sourceIdentityHash, StringComparison.OrdinalIgnoreCase));

    private async Task<bool> UpdatePictureMetadataAsync(Picture picture, AkeneoResolvedAsset asset)
    {
        var seoName = await ResolveSeoFilenameAsync(asset);
        if (string.Equals(picture.SeoFilename, seoName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(picture.AltAttribute, asset.AltText, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(picture.TitleAttribute, asset.TitleText, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        picture.SeoFilename = seoName;
        picture.AltAttribute = asset.AltText;
        picture.TitleAttribute = asset.TitleText;
        await pictureService.UpdatePictureAsync(picture);
        return true;
    }

    /// <summary>
    /// Deletes the row, then its picture and gallery entry when no other row,
    /// and no other combination of the product, still uses the picture.
    /// </summary>
    private async Task<bool> ReleaseRowAsync(AkeneoManagedAsset row)
    {
        await managedAssetService.DeleteAsync(row);
        return row.NopPictureId > 0 &&
            await DeletePictureIfUnusedAsync(row.NopPictureId.Value, row.NopProductId, row.NopProductAttributeCombinationId);
    }

    /// <param name="releasedCombinationId">
    /// The combination giving the picture up; its own links don't keep it.
    /// </param>
    private async Task<bool> DeletePictureIfUnusedAsync(
        int pictureId,
        int productId,
        int? releasedCombinationId = null)
    {
        if ((await managedAssetService.GetByPictureAsync(pictureId)).Count > 0)
            return false;

        var releasedLinks = new List<ProductAttributeCombinationPicture>();
        foreach (var combination in await productAttributeService.GetAllProductAttributeCombinationsAsync(productId))
        {
            var links = (await productAttributeService.GetProductAttributeCombinationPicturesAsync(combination.Id))
                .Where(link => link.PictureId == pictureId)
                .ToList();
            if (links.Count == 0)
                continue;

            if (combination.Id != releasedCombinationId)
                return false; // another combination (perhaps by hand) shows it

            releasedLinks.AddRange(links);
        }

        // nopCommerce has no foreign key from these links to the picture.
        foreach (var link in releasedLinks)
            await productAttributeService.DeleteProductAttributeCombinationPictureAsync(link);

        var changed = releasedLinks.Count > 0;
        foreach (var relation in (await productService.GetProductPicturesByProductIdAsync(productId))
                     .Where(item => item.PictureId == pictureId)
                     .ToList())
        {
            await productService.DeleteProductPictureAsync(relation);
            changed = true;
        }

        var picture = await pictureService.GetPictureByIdAsync(pictureId);
        if (picture != null)
        {
            await pictureService.DeletePictureAsync(picture);
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Rows whose combination was deleted outside the plugin (for example in
    /// the admin) are released the next time a combination of the parent syncs.
    /// </summary>
    private async Task<bool> ReleaseDeletedCombinationsAsync(
        int productId,
        CancellationToken cancellationToken)
    {
        var orphaned = (await managedAssetService.GetAllByProductAsync(productId))
            .Where(item => item.NopProductAttributeCombinationId > 0)
            .ToList();
        if (orphaned.Count == 0)
            return false;

        var existing = (await productAttributeService.GetAllProductAttributeCombinationsAsync(productId))
            .Select(item => item.Id)
            .ToHashSet();
        var changed = false;
        foreach (var row in orphaned.Where(item => !existing.Contains(item.NopProductAttributeCombinationId.Value)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            changed |= await ReleaseRowAsync(row);
        }

        return changed;
    }

    /// <summary>
    /// Gallery entries the product's combinations use, which product-level
    /// Replace all must keep.
    /// </summary>
    private async Task<HashSet<int>> GetCombinationProductPictureIdsAsync(int productId) =>
        (await managedAssetService.GetAllByProductAsync(productId))
            .Where(item => item.NopProductAttributeCombinationId > 0 && item.NopProductPictureId > 0)
            .Select(item => item.NopProductPictureId.Value)
            .ToHashSet();

    private async Task<bool> IsPictureUsedByOtherRowsAsync(AkeneoManagedAsset managed) =>
        managed.NopPictureId > 0 &&
        (await managedAssetService.GetByPictureAsync(managed.NopPictureId.Value))
            .Any(item => item.Id != managed.Id);

    /// <summary>
    /// Downloads the combination's images, skipping versions a row on the
    /// parent already holds.
    /// </summary>
    private async Task PrepareCombinationAsync(
        AkeneoProductSyncContext context,
        CancellationToken cancellationToken)
    {
        if (context.PreloadedAssetBinaries == null)
            return;

        var desired = await ResolveDesiredAsync(context, warningSink: null, cancellationToken: cancellationToken);
        if (desired.AuthoritativeResolutionFailed)
            return;

        var parentId = context.ExistingSyncState.NopProductId;
        var held = parentId > 0
            ? await managedAssetService.GetAllByProductAsync(parentId)
            : Array.Empty<AkeneoManagedAsset>();

        foreach (var asset in SelectCombinationAssets(desired.ResolvedMappings, warningSink: null))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var key = BuildBinaryCacheKey(asset);
            if (key == null || context.PreloadedAssetBinaries.ContainsKey(key))
                continue;

            if (held.Any(item =>
                    item.NopPictureId > 0 &&
                    string.Equals(item.SourceIdentityHash, asset.SourceIdentityHash, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(item.SourceFingerprint, asset.SourceFingerprint, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            try
            {
                context.PreloadedAssetBinaries[key] = await DownloadFromSourceAsync(asset, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Non-fatal: the write phase downloads inline and warns there.
            }
        }
    }

    /// <summary>
    /// Dry-run plan for a combination leaf, from the same selection the write
    /// path uses. Nothing is downloaded or changed.
    /// </summary>
    private async Task PlanCombinationAsync(
        AkeneoProductSyncContext context,
        AkeneoProductMappingPreviewModel model,
        CancellationToken cancellationToken)
    {
        const string target = "Combination picture";
        var syncMode = context.Request.AssetSyncMode;
        var combinationId = context.ExistingSyncState.NopProductAttributeCombinationId.Value;
        var links = await productAttributeService.GetProductAttributeCombinationPicturesAsync(combinationId);

        var desired = await ResolveDesiredAsync(context, warning => model.Warnings.Add(warning), cancellationToken);
        if (desired.ActiveMappings.Count == 0 && links.Count == 0)
            return;

        if (desired.AuthoritativeResolutionFailed)
        {
            AddReviewOperation(
                model,
                "Assets",
                target,
                "Asset mappings",
                "The combination's pictures are left unchanged because an asset mapping could not be resolved safely.");
            return;
        }

        var assets = SelectCombinationAssets(desired.ResolvedMappings, warning => model.Warnings.Add(warning));
        var rows = await managedAssetService.GetByCombinationAsync(combinationId);
        var kept = new List<AkeneoManagedAsset>();

        foreach (var asset in assets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = rows.FirstOrDefault(item =>
                string.Equals(item.AssetMappingKey, asset.Mapping.MappingKey, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.SourceIdentityHash, asset.SourceIdentityHash, StringComparison.OrdinalIgnoreCase));

            var details = new List<string>();
            if (row == null)
                details.Add("the image is added to the parent's gallery (or an identical picture there is reused) and linked to the combination");
            else
            {
                kept.Add(row);
                if (!string.Equals(row.SourceFingerprint, asset.SourceFingerprint, StringComparison.OrdinalIgnoreCase))
                    details.Add("source fingerprint changed");
                if (!links.Any(link => link.PictureId == row.NopPictureId))
                    details.Add("the picture is linked to the combination again");
                if (row.NopPictureId is not > 0 || await pictureService.GetPictureByIdAsync(row.NopPictureId.Value) == null)
                    details.Add("managed nopCommerce picture is missing");
            }

            AddOperation(
                model,
                "Assets",
                $"{target}: {GetAssetDisplayName(asset)}",
                GetMappingSource(asset.Mapping),
                row == null ? null : DescribeManagedAssetValue(row),
                DescribeResolvedAssetValue(asset),
                row == null
                    ? AkeneoDryRunOperationType.Add
                    : details.Count == 0 ? AkeneoDryRunOperationType.NoChange : AkeneoDryRunOperationType.Update,
                details.Count == 0
                    ? "The combination already shows the managed picture of this image."
                    : string.Join("; ", details) + ".");
        }

        var keptIds = kept.Select(row => row.Id).ToHashSet();
        var stale = rows.Where(row => !keptIds.Contains(row.Id)).ToList();

        foreach (var link in CombinationLinksToRemove(syncMode, links, kept, stale, desired.ActiveMappings))
        {
            AddOperation(
                model,
                "Assets",
                $"{target} link: picture #{link.PictureId}",
                "Asset mappings",
                $"Picture #{link.PictureId}",
                null,
                AkeneoDryRunOperationType.Remove,
                syncMode == AkeneoCollectionSyncMode.ReplaceAll
                    ? "Replace All removes every combination picture that is not one of the variant's images in Akeneo."
                    : "The image is no longer the variant's; its picture is deleted when nothing else uses it.");
        }

        model.CoverageNotes.Add(
            "This variant is an attribute combination: the images of its Variant products asset mappings are added to the parent product's gallery and linked to the combination.");
    }
}
