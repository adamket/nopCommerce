using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

/// <summary>
/// Syncs nopCommerce cross-sell products from the profile's <c>CrossSellAssociationType</c>,
/// exactly as related products are synced. nopCommerce cross-sells have no display order, so
/// new links simply follow the existing ones. See <see cref="AkeneoProductAssociationSynchronizer"/>.
/// </summary>
public class AkeneoProductCrossSellSynchronizer(
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
    public override int Order => 710;

    public override string Area => "Cross-sell products";

    protected override string Noun => "cross-sell product";

    protected override string LinkedValue => "Cross-sell";

    protected override string NotLinkedValue => "Not a cross-sell";

    protected override AkeneoManagedRelationType RelationType => AkeneoManagedRelationType.CrossSellProduct;

    protected override AkeneoCollectionSyncMode GetMode(AkeneoProductImportRequest request) =>
        request.CrossSellSyncMode;

    protected override string GetAssociationType(AkeneoProductImportRequest request) =>
        request.CrossSellAssociationType;

    protected override async Task<IList<AssociationLink>> GetLinksAsync(int productId) =>
        (await ProductService.GetCrossSellProductsByProductId1Async(productId, showHidden: true))
        .Select(crossSell => new AssociationLink(crossSell.Id, crossSell.ProductId2, 0, crossSell))
        .ToList();

    protected override async Task<int> InsertLinkAsync(int productId, int targetProductId, int displayOrder)
    {
        var crossSell = new CrossSellProduct
        {
            ProductId1 = productId,
            ProductId2 = targetProductId
        };

        await ProductService.InsertCrossSellProductAsync(crossSell);
        return crossSell.Id;
    }

    protected override Task DeleteLinkAsync(AssociationLink link) =>
        ProductService.DeleteCrossSellProductAsync((CrossSellProduct)link.Entity);
}
