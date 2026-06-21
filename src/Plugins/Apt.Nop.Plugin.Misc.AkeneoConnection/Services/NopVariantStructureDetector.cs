using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
public class NopVariantStructureDetector : INopVariantStructureDetector
{
    private readonly IProductService _productService;
    private readonly IProductAttributeService _productAttributeService;

    public NopVariantStructureDetector(
        IProductService productService,
        IProductAttributeService productAttributeService)
    {
        _productService = productService;
        _productAttributeService = productAttributeService;
    }

    public async Task<NopVariantStructureDetectionResult> DetectAsync(Product parentProduct)
    {
        var matches = new List<NopVariantStructureDetectionResult>();

        var associatedProducts = await _productService.GetAssociatedProductsAsync(
            parentGroupedProductId: parentProduct.Id,
            showHidden: true);

        if (associatedProducts.Any())
        {
            matches.Add(new NopVariantStructureDetectionResult
            {
                HasVariantStructure = true,
                Mode = AkeneoVariantRelationshipMode.GroupedProducts
            });
        }

        var mappings =
            await _productAttributeService.GetProductAttributeMappingsByProductIdAsync(parentProduct.Id);

        foreach (var mapping in mappings)
        {
            var values = await _productAttributeService.GetProductAttributeValuesAsync(mapping.Id);

            if (values.Any(x =>
                    x.AttributeValueTypeId == (int)AttributeValueType.AssociatedToProduct &&
                    x.AssociatedProductId > 0))
            {
                matches.Add(new NopVariantStructureDetectionResult
                {
                    HasVariantStructure = true,
                    Mode = AkeneoVariantRelationshipMode.AssociatedToProductAttributeValue,
                    AssociatedProductAttributeId = mapping.ProductAttributeId
                });

                break;
            }
        }

        var combinations =
            await _productAttributeService.GetAllProductAttributeCombinationsAsync(parentProduct.Id);

        if (combinations.Any())
        {
            matches.Add(new NopVariantStructureDetectionResult
            {
                HasVariantStructure = true,
                Mode = AkeneoVariantRelationshipMode.ProductAttributeCombinations
            });
        }

        if (matches.Count == 0)
        {
            return new NopVariantStructureDetectionResult
            {
                HasVariantStructure = false
            };
        }

        if (matches.Count > 1)
        {
            return new NopVariantStructureDetectionResult
            {
                HasVariantStructure = true,
                IsAmbiguous = true
            };
        }

        return matches[0];
    }
}