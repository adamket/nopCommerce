using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

/// <summary>
/// Syncs nopCommerce related products from the profile's <c>RelatedProductAssociationType</c>.
/// See <see cref="AkeneoProductAssociationSynchronizer"/> for how targets are resolved and linked.
/// </summary>
public class AkeneoProductRelatedProductSynchronizer(
    IProductService productService,
    IAkeneoNopEntityMappingService entityMappingService,
    IAkeneoProductSyncStateService syncStateService,
    IAkeneoManagedRelationService managedRelationService,
    IProductAttributeService productAttributeService = null)
    : AkeneoProductAssociationSynchronizer(
        productService,
        entityMappingService,
        syncStateService,
        managedRelationService,
        productAttributeService)
{
    public override int Order => 700;

    public override string Area => "Related products";

    protected override string Noun => "related product";

    protected override string LinkedValue => "Related";

    protected override string NotLinkedValue => "Not related";

    protected override AkeneoManagedRelationType RelationType => AkeneoManagedRelationType.RelatedProduct;

    protected override AkeneoCollectionSyncMode GetMode(AkeneoProductImportRequest request) =>
        request.RelatedProductSyncMode;

    protected override string GetAssociationType(AkeneoProductImportRequest request) =>
        request.RelatedProductAssociationType;

    protected override async Task<IList<AssociationLink>> GetLinksAsync(int productId) =>
        (await ProductService.GetRelatedProductsByProductId1Async(productId, showHidden: true))
        .Select(related => new AssociationLink(related.Id, related.ProductId2, related.DisplayOrder, related))
        .ToList();

    protected override async Task<int> InsertLinkAsync(int productId, int targetProductId, int displayOrder)
    {
        var related = new RelatedProduct
        {
            ProductId1 = productId,
            ProductId2 = targetProductId,
            DisplayOrder = displayOrder
        };

        await ProductService.InsertRelatedProductAsync(related);
        return related.Id;
    }

    protected override Task DeleteLinkAsync(AssociationLink link) =>
        ProductService.DeleteRelatedProductAsync((RelatedProduct)link.Entity);
}
