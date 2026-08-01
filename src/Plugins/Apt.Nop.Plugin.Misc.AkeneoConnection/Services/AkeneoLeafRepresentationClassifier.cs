using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Nop.Core.Domain.Catalog;
using Nop.Data;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public sealed class AkeneoLeafRepresentationClassifier(
    IProductAttributeService productAttributeService,
    IRepository<ProductAttributeValue> productAttributeValueRepository)
    : IAkeneoLeafRepresentationClassifier
{
    public async Task<AkeneoVariantRelationshipMode?> ClassifyAsync(
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
}
