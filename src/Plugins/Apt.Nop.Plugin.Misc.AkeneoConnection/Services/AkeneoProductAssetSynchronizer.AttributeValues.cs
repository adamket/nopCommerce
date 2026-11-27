using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Assets;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Media;
using static Apt.Nop.Plugin.Misc.AkeneoConnection.Services.DryRun.AkeneoDryRunPlanHelper;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public sealed partial class AkeneoProductAssetSynchronizer
{
    private static bool IsValuePictureMapping(AkeneoAssetMapping mapping) =>
        mapping.DestinationTypeId == (int)AkeneoAssetDestinationType.ProductAttributeValuePictures;

    private async Task<ProductAttributeValue> ResolvePictureValueAsync(
        AkeneoProductSyncContext context, AkeneoAssetMapping mapping, Product product,
        CancellationToken cancellationToken)
    {
        if (attributeValuePictures == null)
            throw new NopException("Attribute value picture storage is unavailable on this installation.");
        if (IsCombinationLeaf(context))
            throw new NopException("Attribute value pictures currently require a variant represented by its own nopCommerce product. A combination must not select a shared value implicitly.");
        if (product is not { Id: > 0 })
            throw new NopException("The destination product and its attribute value must exist before pictures can be bound.");
        var attribute = (await productAttributeService.GetProductAttributeMappingsByProductIdAsync(product.Id))
            .SingleOrDefault(item => item.ProductAttributeId == mapping.TargetProductAttributeId);
        if (attribute == null)
            throw new NopException($"Product #{product.Id} has no mapping for product attribute #{mapping.TargetProductAttributeId}. Existing pictures were preserved.");
        string key = null;
        if (!string.IsNullOrWhiteSpace(mapping.AttributeValueKeyTemplate))
        {
            if (AkeneoAssociatedValueNameTemplate.TryGetLiteral(mapping.AttributeValueKeyTemplate, out var literal))
                key = literal.Trim();
            else
            {
                if (valuePictureTemplateRenderer == null)
                    throw new NopException("The attribute value key template renderer is unavailable.");
                var rendered = await valuePictureTemplateRenderer.RenderAsync(mapping.AttributeValueKeyTemplate,
                    new AkeneoValueTemplateContext
                    {
                        Source = context.Source, Locale = context.Request.Locale, Channel = context.Request.Channel,
                        Currency = context.Request.Currency, FamilyCode = context.MappingFamilyCode,
                        FamilyVariantCode = context.MappingFamilyVariantCode, Sku = context.Sku
                    }, cancellationToken);
                if (!rendered.Success || string.IsNullOrWhiteSpace(rendered.Value))
                    throw new NopException("The attribute value key template did not produce a complete, nonempty key. Existing pictures were preserved.");
                key = rendered.Value.Trim();
            }
        }
        return AkeneoAttributeValuePicturePlan.SelectValue(
            await productAttributeService.GetProductAttributeValuesAsync(attribute.Id), key);
    }

    private async Task<bool> ApplyValuePicturesAsync(AkeneoProductSyncContext context,
        AkeneoAssetMapping mapping, IReadOnlyList<AkeneoResolvedAsset> assets, CancellationToken cancellationToken)
    {
        var value = await ResolvePictureValueAsync(context, mapping, context.Product, cancellationToken);
        var allRows = await managedAssetService.GetAllByProductAsync(context.Product.Id);
        if (allRows.Any(row => row.NopProductAttributeValueId == value.Id && row.AssetMappingKey != mapping.MappingKey))
            throw new NopException($"Attribute value #{value.Id} is already targeted by another managed asset mapping. Use one mapping per value.");
        var oldRows = allRows.Where(row => row.AssetMappingKey == mapping.MappingKey && row.NopProductAttributeCombinationId == null).ToList();
        if (oldRows.Any(row => row.DestinationTypeId != mapping.DestinationTypeId))
            throw new NopException("Create a separate attribute value picture mapping instead of changing an existing destination with managed assets.");
        var current = await attributeValuePictures.GetPictureIdsAsync(value);
        var staged = await StageValuePicturesAsync(context, assets, oldRows, cancellationToken);

        var owned = oldRows.Where(row => row.NopProductAttributeValueId == value.Id && row.OwnsAttributeValuePicture && row.NopPictureId > 0)
            .Select(row => row.NopPictureId.Value).ToHashSet();
        var desiredIds = staged.Select(item => item.Picture.Id).Distinct().ToList();
        var replaceAll = context.Request.AssetSyncMode == AkeneoCollectionSyncMode.ReplaceAll;
        if (mapping.AdoptExistingValuePictures || replaceAll)
            owned.UnionWith(desiredIds);
        var next = AkeneoAttributeValuePicturePlan.Reconcile(current, desiredIds, owned,
            context.Request.AssetSyncMode == AkeneoCollectionSyncMode.Merge, replaceAll);
        var changed = !current.SequenceEqual(next);
        if (changed)
            await attributeValuePictures.SetPictureIdsAsync(value, next);

        foreach (var item in staged)
        {
            // Source ordering is carried by the product gallery on both supported sites.
            if (item.Gallery.DisplayOrder != item.Asset.DisplayOrder)
            {
                item.Gallery.DisplayOrder = item.Asset.DisplayOrder;
                await productService.UpdateProductPictureAsync(item.Gallery);
                changed = true;
            }
            // Only update metadata on pictures this mapping already owns, or during explicit adoption.
            if ((item.Previous?.OwnsAttributeValuePicture == true || mapping.AdoptExistingValuePictures) && !await IsPictureUsedByOtherRowsAsync(item.Previous ?? new AkeneoManagedAsset { NopPictureId = item.Picture.Id }))
                changed |= await UpdatePictureMetadataAsync(item.Picture, item.Asset);
            if (item.Previous?.NopProductAttributeValueId > 0 &&
                item.Previous.NopProductAttributeValueId != value.Id)
                changed |= await ReleaseValuePictureAsync(item.Previous);
            var row = item.Previous ?? new AkeneoManagedAsset();
            var owns = owned.Contains(item.Picture.Id) || !current.Contains(item.Picture.Id);
            row.NopProductAttributeValueId = value.Id;
            row.OwnsAttributeValuePicture = owns;
            await UpsertManagedAsync(context, item.Asset, row.Id == 0 ? InitializeValueAssetRow(row, context, item.Asset) : row,
                context.Request.SyncProfileId.GetValueOrDefault(), nopPictureId: item.Picture.Id,
                nopProductPictureId: item.Gallery.Id, destinationKey: $"Attribute value #{value.Id}");
        }
        if (context.Request.AssetSyncMode != AkeneoCollectionSyncMode.Merge)
        {
            var identities = staged.Select(item => item.Asset.SourceIdentityHash).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var stale in oldRows.Where(row => !identities.Contains(row.SourceIdentityHash)))
            {
                // Reconcile already removed stale pictures on the selected value. Other old targets need release.
                if (stale.NopProductAttributeValueId != value.Id)
                    changed |= await ReleaseValuePictureAsync(stale);
                await managedAssetService.DeleteAsync(stale);
            }
        }
        if (changed)
        {
            context.MarkChanged();
            context.ProductUpdateEventPending = true;
            context.Result.AddMessage($"Synchronized {desiredIds.Count} pictures for product #{context.Product.Id}, attribute value '{value.Name}' (#{value.Id}).");
        }
        return true;
    }

    private sealed record StagedValuePicture(AkeneoResolvedAsset Asset, Picture Picture,
        ProductPicture Gallery, AkeneoManagedAsset Previous);

    private async Task<List<StagedValuePicture>> StageValuePicturesAsync(AkeneoProductSyncContext context,
        IReadOnlyList<AkeneoResolvedAsset> assets, IList<AkeneoManagedAsset> oldRows, CancellationToken cancellationToken)
    {
        var staged = new List<StagedValuePicture>();

        // Resolve and validate every binary before changing any attribute-value association or ownership row.
        foreach (var asset in assets.OrderBy(item => item.DisplayOrder))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var previous = oldRows.FirstOrDefault(row => row.SourceIdentityHash == asset.SourceIdentityHash);
            var picture = previous?.NopPictureId > 0 ? await pictureService.GetPictureByIdAsync(previous.NopPictureId.Value) : null;
            var gallery = previous?.NopProductPictureId > 0 ? await productService.GetProductPictureByIdAsync(previous.NopProductPictureId.Value) : null;
            if (gallery?.ProductId != context.Product.Id || gallery?.PictureId != picture?.Id)
                gallery = null;
            if (picture == null || previous?.SourceFingerprint != asset.SourceFingerprint)
            {
                var file = await DownloadAsync(context, asset, cancellationToken);
                var mime = file.ContentType ?? asset.MimeType;
                if (file.Bytes is not { Length: > 0 } || mime?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) != true)
                    throw new NopException($"Asset '{asset.AssetCode ?? asset.MediaFileCode}' is not a valid image. Existing value pictures were preserved.");
                var identical = await FindIdenticalGalleryPictureAsync(context.Product.Id, file.Bytes);
                if (identical != null)
                    (gallery, picture) = identical.Value;
                else
                {
                    picture = await pictureService.InsertPictureAsync(file.Bytes, mime,
                        await ResolveSeoFilenameAsync(asset, file.FileName), asset.AltText, asset.TitleText);
                    gallery = null; // Never overwrite a binary shared with manual pictures or another value.
                }
            }
            if (gallery == null)
            {
                gallery = new ProductPicture { ProductId = context.Product.Id, PictureId = picture.Id, DisplayOrder = asset.DisplayOrder };
                await productService.InsertProductPictureAsync(gallery);
                context.MarkChanged();
                context.ProductUpdateEventPending = true;
            }
            staged.Add(new StagedValuePicture(asset, picture, gallery, previous));
        }

        return staged;
    }

    private static AkeneoManagedAsset InitializeValueAssetRow(AkeneoManagedAsset row,
        AkeneoProductSyncContext context, AkeneoResolvedAsset asset)
    {
        row.NopProductId = context.Product.Id;
        row.SourceIdentityHash = asset.SourceIdentityHash;
        row.CreatedOnUtc = DateTime.UtcNow;
        return row;
    }

    private async Task<bool> ReleaseValuePictureAsync(AkeneoManagedAsset row)
    {
        if (attributeValuePictures == null || !row.OwnsAttributeValuePicture || row.NopProductAttributeValueId is not > 0 || row.NopPictureId is not > 0)
            return false;
        if ((await managedAssetService.GetByPictureAsync(row.NopPictureId.Value)).Any(other =>
                other.Id != row.Id && other.NopProductAttributeValueId == row.NopProductAttributeValueId))
            return false;
        var value = await productAttributeService.GetProductAttributeValueByIdAsync(row.NopProductAttributeValueId.Value);
        if (value == null)
            return false;
        var ids = await attributeValuePictures.GetPictureIdsAsync(value);
        if (!ids.Contains(row.NopPictureId.Value))
            return false;
        await attributeValuePictures.SetPictureIdsAsync(value, ids.Where(id => id != row.NopPictureId.Value).ToList());
        // Association ownership does not imply ownership of the gallery or binary. Retain both for reuse.
        return true;
    }

    private async Task PlanValuePicturesAsync(AkeneoProductSyncContext context, AkeneoProductMappingPreviewModel model,
        ResolvedAssetMapping resolved, CancellationToken cancellationToken)
    {
        try
        {
            var value = await ResolvePictureValueAsync(context, resolved.Mapping, context.ExistingProduct, cancellationToken);
            var current = await attributeValuePictures.GetPictureIdsAsync(value);
            var rows = await managedAssetService.GetByProductAndMappingAsync(context.ExistingProduct.Id, resolved.Mapping.MappingKey);
            var desired = resolved.Assets.Select(asset => asset.SourceIdentityHash).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var asset in resolved.Assets)
            {
                var row = rows.FirstOrDefault(item => item.SourceIdentityHash == asset.SourceIdentityHash && item.NopProductAttributeValueId == value.Id);
                var same = row?.NopPictureId > 0 && current.Contains(row.NopPictureId.Value) && row.SourceFingerprint == asset.SourceFingerprint && row.DisplayOrder == asset.DisplayOrder && (!resolved.Mapping.AdoptExistingValuePictures || row.OwnsAttributeValuePicture);
                AddOperation(model, "Assets", $"Product #{context.ExistingProduct.Id}, {value.Name} (value #{value.Id})",
                    GetMappingSource(resolved.Mapping), row?.NopPictureId?.ToString(), GetAssetDisplayName(asset),
                    same ? AkeneoDryRunOperationType.NoChange : AkeneoDryRunOperationType.Update,
                    resolved.Mapping.AdoptExistingValuePictures ? "Import/reuse this image and adopt its existing value association. Binaries are checked during sync." : "Import/reuse this image; preserve existing manual value associations. Binaries are checked during sync.");
            }
            if (context.Request.AssetSyncMode != AkeneoCollectionSyncMode.Merge)
            {
                foreach (var row in rows.Where(item => item.OwnsAttributeValuePicture && (!desired.Contains(item.SourceIdentityHash) || item.NopProductAttributeValueId != value.Id)))
                    AddOperation(model, "Assets", $"Attribute value #{row.NopProductAttributeValueId}", GetMappingSource(resolved.Mapping),
                        $"Picture #{row.NopPictureId}", null, AkeneoDryRunOperationType.Remove, "Remove this stale managed value association; retain the gallery picture and binary.");
            }
            if (context.Request.AssetSyncMode == AkeneoCollectionSyncMode.ReplaceAll)
                AddReviewOperation(model, "Assets", $"Attribute value #{value.Id}", GetMappingSource(resolved.Mapping),
                    "Replace all replaces this value's complete picture set, including manual associations, after every image resolves. Other values and gallery entries remain intact.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AddReviewOperation(model, "Assets", GetMappingDisplayName(resolved.Mapping), GetMappingSource(resolved.Mapping), exception.Message);
        }
    }
}
