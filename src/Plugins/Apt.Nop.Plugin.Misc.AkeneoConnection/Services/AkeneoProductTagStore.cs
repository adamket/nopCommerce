using Nop.Core.Caching;
using Nop.Core.Domain.Catalog;
using Nop.Data;
using Nop.Services.Catalog;
using Nop.Services.Seo;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

/// <summary>One product–tag link: the mapping row and the tag it points to.</summary>
public sealed record AkeneoProductTagLink(ProductProductTagMapping Mapping, ProductTag Tag);

/// <summary>
/// The individual product-tag reads and writes the tag section needs. nopCommerce's own tag
/// service can only replace a product's whole tag list, which would make Merge and Replace
/// managed impossible, so links are added and removed one at a time here.
/// </summary>
public interface IAkeneoProductTagStore
{
    Task<IList<AkeneoProductTagLink>> GetProductTagLinksAsync(int productId);

    /// <summary>The tag with this name, compared ignoring case as nopCommerce does; null when there is none.</summary>
    Task<ProductTag> FindTagByNameAsync(string name);

    /// <summary>Creates a tag with its URL slug, as nopCommerce does when a tag is first used.</summary>
    Task<ProductTag> CreateTagAsync(string name);

    Task<ProductProductTagMapping> AddLinkAsync(int productId, int productTagId);

    Task RemoveLinkAsync(ProductProductTagMapping mapping);
}

public sealed class AkeneoProductTagStore(
    IRepository<ProductTag> tagRepository,
    IRepository<ProductProductTagMapping> mappingRepository,
    IProductTagService productTagService,
    IUrlRecordService urlRecordService,
    IStaticCacheManager staticCacheManager) : IAkeneoProductTagStore
{
    public async Task<IList<AkeneoProductTagLink>> GetProductTagLinksAsync(int productId)
    {
        var mappings = await mappingRepository.Table
            .Where(mapping => mapping.ProductId == productId)
            .ToListAsync();

        if (mappings.Count == 0)
            return new List<AkeneoProductTagLink>();

        var tags = (await productTagService.GetProductTagsByIdsAsync(
                mappings.Select(mapping => mapping.ProductTagId).Distinct().ToArray()))
            .ToDictionary(tag => tag.Id);

        return mappings
            .Where(mapping => tags.ContainsKey(mapping.ProductTagId))
            .Select(mapping => new AkeneoProductTagLink(mapping, tags[mapping.ProductTagId]))
            .ToList();
    }

    public async Task<ProductTag> FindTagByNameAsync(string name)
    {
        var lowered = name.Trim().ToLowerInvariant();

        return await tagRepository.Table
            .Where(tag => tag.Name.ToLower() == lowered)
            .OrderBy(tag => tag.Id)
            .FirstOrDefaultAsync();
    }

    public async Task<ProductTag> CreateTagAsync(string name)
    {
        var tag = new ProductTag { Name = name.Trim() };
        await tagRepository.InsertAsync(tag);

        var seName = await urlRecordService.ValidateSeNameAsync(tag, string.Empty, tag.Name, true);
        await urlRecordService.SaveSlugAsync(tag, seName, 0);

        await ClearTagCacheAsync();
        return tag;
    }

    public async Task<ProductProductTagMapping> AddLinkAsync(int productId, int productTagId)
    {
        var mapping = new ProductProductTagMapping
        {
            ProductId = productId,
            ProductTagId = productTagId
        };

        await productTagService.InsertProductProductTagMappingAsync(mapping);
        await ClearTagCacheAsync();
        return mapping;
    }

    public async Task RemoveLinkAsync(ProductProductTagMapping mapping)
    {
        await mappingRepository.DeleteAsync(mapping);
        await ClearTagCacheAsync();
    }

    // Product tag lists and tag counts are cached under the tag prefix, as in
    // ProductTagService.UpdateProductTagsAsync.
    private Task ClearTagCacheAsync() =>
        staticCacheManager.RemoveByPrefixAsync(NopEntityCacheDefaults<ProductTag>.Prefix);
}
