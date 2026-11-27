using Nop.Core.Domain.Catalog;
using Nop.Data;
using Nop.Services.Catalog;


namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public sealed class AkeneoAttributeValuePictureStore(
    IProductAttributeService attributes,
    
    IRepository<ProductAttributeValuePicture> valuePictures,
    IRepository<ProductAttributeValue> values,
    IRepository<ProductAttributeMapping> mappings,
    IRepository<ProductPicture> gallery,
    IRepository<ProductAttributeCombinationPicture> combinationPictures) : IAkeneoAttributeValuePictureStore
{
    public async Task<IList<int>> GetPictureIdsAsync(ProductAttributeValue value)
    {
        var pictures = await attributes.GetProductAttributeValuePicturesAsync(value.Id);
        var mapping = await attributes.GetProductAttributeMappingByIdAsync(value.ProductAttributeMappingId);
        var order = await gallery.Table.Where(item => item.ProductId == mapping.ProductId).ToListAsync();
        return pictures.OrderBy(picture => order.FirstOrDefault(item => item.PictureId == picture.PictureId)?.DisplayOrder ?? int.MaxValue)
            .ThenBy(picture => picture.PictureId).Select(picture => picture.PictureId).Distinct().ToList();
    }

    public async Task SetPictureIdsAsync(ProductAttributeValue value, IReadOnlyList<int> pictureIds)
    {
        var wanted = pictureIds.Where(id => id > 0).Distinct().ToList();
        
        // Query the full table: legacy read APIs hide links absent from the gallery.
        var existing = await valuePictures.Table.Where(picture => picture.ProductAttributeValueId == value.Id).ToListAsync();
        foreach (var stale in existing.Where(picture => !wanted.Contains(picture.PictureId)))
            await attributes.DeleteProductAttributeValuePicturesAsync(new List<ProductAttributeValuePicture> { stale });
        foreach (var id in wanted.Where(id => existing.All(picture => picture.PictureId != id)))
            await attributes.InsertProductAttributeValuePictureAsync(new ProductAttributeValuePicture { ProductAttributeValueId = value.Id, PictureId = id });
    }

    public async Task<bool> IsReferencedAsync(int pictureId, int? excludingProductId = null)
    {
        if (await valuePictures.Table.AnyAsync(picture => picture.PictureId == pictureId)) return true;
        if (await combinationPictures.Table.AnyAsync(picture => picture.PictureId == pictureId)) return true;
        
        return await gallery.Table.AnyAsync(picture => picture.PictureId == pictureId &&
            (!excludingProductId.HasValue || picture.ProductId != excludingProductId.Value));
    }

    public async Task<HashSet<int>> GetProductPictureIdsAsync(int productId)
    {
        var productValues = await (from value in values.Table join mapping in mappings.Table on value.ProductAttributeMappingId equals mapping.Id
            where mapping.ProductId == productId select value).ToListAsync();
        var ids = new HashSet<int>();
        foreach (var value in productValues)
            ids.UnionWith(await GetPictureIdsAsync(value));
        return (await gallery.Table.Where(picture => picture.ProductId == productId).ToListAsync())
            .Where(picture => ids.Contains(picture.PictureId)).Select(picture => picture.Id).ToHashSet();
    }
}
