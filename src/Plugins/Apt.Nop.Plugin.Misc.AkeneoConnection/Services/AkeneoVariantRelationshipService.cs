using System.Xml.Linq;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
public class AkeneoVariantRelationshipService(
    IAkeneoVariantRelationshipResolver relationshipResolver,
    IProductService productService,
    IProductAttributeService productAttributeService,
    IAkeneoProductValueResolver productValueResolver) : IAkeneoVariantRelationshipService
{
    public async Task<AkeneoVariantImportResult> ApplyAsync(
        Product parentProduct,
        AkeneoVariantImportContext context,
        Func<Task<Product>> upsertChildProductAsync)
    {
        var resolution = await relationshipResolver.ResolveAsync(parentProduct, context.AkeneoFamilyCode);

        // Existing structure always wins; the override only applies when there's nothing to preserve.
        var effectiveMode =
            resolution.Source == AkeneoVariantRelationshipSource.ExistingNopParent
                ? resolution.Mode
                : context.VariantRelationshipModeOverride ?? resolution.Mode;

        context.Options = resolution.Options ?? new AkeneoVariantRelationshipOptions
        {
            Enabled = true,
            AkeneoFamilyCode = context.AkeneoFamilyCode,
            Mode = effectiveMode,
            PreserveExistingNopVariantStructure = true
        };
        context.Options.Mode = effectiveMode;

        await EnsureParentShapeAsync(parentProduct, effectiveMode);
        return effectiveMode switch
        {
            AkeneoVariantRelationshipMode.GroupedProducts =>
                await ApplyGroupedProductAsync(
                    parentProduct,
                    context,
                    resolution.Source,
                    upsertChildProductAsync),

            AkeneoVariantRelationshipMode.AssociatedToProductAttributeValue =>
                await ApplyAssociatedToProductAttributeValueAsync(
                    parentProduct,
                    context,
                    resolution.Source,
                    upsertChildProductAsync),

            AkeneoVariantRelationshipMode.ProductAttributeCombinations =>
                await ApplyProductAttributeCombinationAsync(
                    parentProduct,
                    context,
                    resolution.Source),

            AkeneoVariantRelationshipMode.None =>
                await ApplyStandaloneProductAsync(
                    context,
                    resolution,
                    upsertChildProductAsync),

            _ => throw new NopException(
                $"Unsupported Akeneo variant relationship mode: {resolution.Mode}")
        };
    }

    private async Task EnsureParentShapeAsync(
        Product parentProduct,
        AkeneoVariantRelationshipMode mode)
    {
        var changed = false;

        if (mode == AkeneoVariantRelationshipMode.GroupedProducts)
        {
            if (parentProduct.ProductType != ProductType.GroupedProduct)
            {
                parentProduct.ProductType = ProductType.GroupedProduct;
                changed = true;
            }

            if (!parentProduct.VisibleIndividually)
            {
                parentProduct.VisibleIndividually = true;
                changed = true;
            }
        }
        else
        {
            if (parentProduct.ProductType != ProductType.SimpleProduct)
            {
                parentProduct.ProductType = ProductType.SimpleProduct;
                changed = true;
            }
        }

        if (mode == AkeneoVariantRelationshipMode.ProductAttributeCombinations &&
            parentProduct.ManageInventoryMethod != ManageInventoryMethod.ManageStockByAttributes)
        {
            parentProduct.ManageInventoryMethod = ManageInventoryMethod.ManageStockByAttributes;
            changed = true;
        }

        if (changed)
            await productService.UpdateProductAsync(parentProduct);
    }

    private async Task<AkeneoVariantImportResult> ApplyGroupedProductAsync(
        Product parentProduct,
        AkeneoVariantImportContext context,
        AkeneoVariantRelationshipSource source,
        Func<Task<Product>> upsertChildProductAsync)
    {
        var childProduct = await upsertChildProductAsync();

        var changed = false;

        if (childProduct.ProductType != ProductType.SimpleProduct)
        {
            childProduct.ProductType = ProductType.SimpleProduct;
            changed = true;
        }

        if (childProduct.ParentGroupedProductId != parentProduct.Id)
        {
            childProduct.ParentGroupedProductId = parentProduct.Id;
            changed = true;
        }

        var visibleIndividually = !context.Options.HideChildProductsWhenRepresentedByParent;

        if (childProduct.VisibleIndividually != visibleIndividually)
        {
            childProduct.VisibleIndividually = visibleIndividually;
            changed = true;
        }

        if (changed)
            await productService.UpdateProductAsync(childProduct);

        return new AkeneoVariantImportResult
        {
            Mode = AkeneoVariantRelationshipMode.GroupedProducts,
            Source = source,
            NopProductId = childProduct.Id
        };
    }

    private async Task<AkeneoVariantImportResult> ApplyAssociatedToProductAttributeValueAsync(
        Product parentProduct,
        AkeneoVariantImportContext context,
        AkeneoVariantRelationshipSource source,
        Func<Task<Product>> upsertChildProductAsync)
    {
        if (!context.Options.AssociatedProductAttributeId.HasValue)
        {
            throw new NopException(
                "Associated-to-product variant import requires AssociatedProductAttributeId.");
        }

        var childProduct = await upsertChildProductAsync();

        if (context.Options.HideChildProductsWhenRepresentedByParent &&
            childProduct.VisibleIndividually)
        {
            childProduct.VisibleIndividually = false;
            await productService.UpdateProductAsync(childProduct);
        }

        var mapping = await EnsureProductAttributeMappingAsync(
            parentProduct.Id,
            context.Options.AssociatedProductAttributeId.Value,
            isRequired: true);

        var values = await productAttributeService.GetProductAttributeValuesAsync(mapping.Id);

        var valueName = BuildAssociatedValueName(context);

        var existingValue = values.FirstOrDefault(x =>
            x.AttributeValueTypeId == (int)AttributeValueType.AssociatedToProduct &&
            x.AssociatedProductId == childProduct.Id);

        if (existingValue == null)
        {
            existingValue = new ProductAttributeValue
            {
                ProductAttributeMappingId = mapping.Id,
                AttributeValueTypeId = (int)AttributeValueType.AssociatedToProduct,
                AssociatedProductId = childProduct.Id,
                Name = valueName,
                DisplayOrder = values.Count + 1
            };

            await productAttributeService.InsertProductAttributeValueAsync(existingValue);
        }
        else if (!string.Equals(existingValue.Name, valueName, StringComparison.Ordinal))
        {
            existingValue.Name = valueName;
            await productAttributeService.UpdateProductAttributeValueAsync(existingValue);
        }

        return new AkeneoVariantImportResult
        {
            Mode = AkeneoVariantRelationshipMode.AssociatedToProductAttributeValue,
            Source = source,
            NopProductId = childProduct.Id
        };
    }

    private async Task<AkeneoVariantImportResult> ApplyProductAttributeCombinationAsync(
        Product parentProduct,
        AkeneoVariantImportContext context,
        AkeneoVariantRelationshipSource source)
    {
        if (context.Options.AxisMappings.Count == 0)
            throw new NopException("Product attribute combination import requires at least one axis mapping.");

        var selections = new List<ProductAttributeSelection>();

        foreach (var axis in context.Options.AxisMappings)
        {
            if (!context.AxisValuesByAkeneoCode.TryGetValue(axis.AkeneoAttributeCode, out var axisValue) ||
                string.IsNullOrWhiteSpace(axisValue))
            {
                if (axis.IsRequired)
                {
                    throw new NopException(
                        $"Akeneo variant is missing required axis value '{axis.AkeneoAttributeCode}'. SKU: {context.Sku}");
                }

                continue;
            }

            var mapping = await EnsureProductAttributeMappingAsync(
                parentProduct.Id,
                axis.NopProductAttributeId,
                axis.IsRequired);

            var attributeValue = await EnsureSimpleProductAttributeValueAsync(
                mapping.Id,
                axisValue);

            selections.Add(new ProductAttributeSelection
            {
                ProductAttributeMappingId = mapping.Id,
                ProductAttributeValueId = attributeValue.Id
            });
        }

        if (selections.Count == 0)
            throw new NopException($"No product attribute selections were created for Akeneo variant SKU '{context.Sku}'.");

        var attributesXml = BuildAttributesXml(selections);

        var combinations =
            await productAttributeService.GetAllProductAttributeCombinationsAsync(parentProduct.Id);

        var existingCombination = combinations.FirstOrDefault(x =>
            string.Equals(x.AttributesXml, attributesXml, StringComparison.OrdinalIgnoreCase));

        if (existingCombination == null)
        {
            existingCombination = new ProductAttributeCombination
            {
                ProductId = parentProduct.Id,
                AttributesXml = attributesXml,
                Sku = context.Sku,
                StockQuantity = context.StockQuantity,
                OverriddenPrice = context.Price
            };

            await productAttributeService.InsertProductAttributeCombinationAsync(existingCombination);
        }
        else
        {
            var changed = false;

            if (!string.Equals(existingCombination.Sku, context.Sku, StringComparison.Ordinal))
            {
                existingCombination.Sku = context.Sku;
                changed = true;
            }

            if (existingCombination.StockQuantity != context.StockQuantity)
            {
                existingCombination.StockQuantity = context.StockQuantity;
                changed = true;
            }

            if (existingCombination.OverriddenPrice != context.Price)
            {
                existingCombination.OverriddenPrice = context.Price;
                changed = true;
            }

            if (changed)
                await productAttributeService.UpdateProductAttributeCombinationAsync(existingCombination);
        }

        return new AkeneoVariantImportResult
        {
            Mode = AkeneoVariantRelationshipMode.ProductAttributeCombinations,
            Source = source,
            NopProductAttributeCombinationId = existingCombination.Id
        };
    }

    private async Task<ProductAttributeMapping> EnsureProductAttributeMappingAsync(
        int productId,
        int productAttributeId,
        bool isRequired)
    {
        var mappings = await productAttributeService.GetProductAttributeMappingsByProductIdAsync(productId);

        var existing = mappings.FirstOrDefault(x => x.ProductAttributeId == productAttributeId);

        if (existing != null)
            return existing;

        var mapping = new ProductAttributeMapping
        {
            ProductId = productId,
            ProductAttributeId = productAttributeId,
            AttributeControlTypeId = (int)AttributeControlType.DropdownList,
            IsRequired = isRequired,
            DisplayOrder = mappings.Count + 1
        };

        await productAttributeService.InsertProductAttributeMappingAsync(mapping);

        return mapping;
    }

    private async Task<ProductAttributeValue> EnsureSimpleProductAttributeValueAsync(
        int productAttributeMappingId,
        string valueName)
    {
        valueName = valueName.Trim();

        var values = await productAttributeService.GetProductAttributeValuesAsync(productAttributeMappingId);

        var existing = values.FirstOrDefault(x =>
            x.AttributeValueTypeId == (int)AttributeValueType.Simple &&
            string.Equals(x.Name, valueName, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
            return existing;

        var value = new ProductAttributeValue
        {
            ProductAttributeMappingId = productAttributeMappingId,
            AttributeValueTypeId = (int)AttributeValueType.Simple,
            Name = valueName,
            DisplayOrder = values.Count + 1
        };

        await productAttributeService.InsertProductAttributeValueAsync(value);

        return value;
    }

    private async Task<AkeneoVariantImportResult> ApplyStandaloneProductAsync(
        AkeneoVariantImportContext context,
        AkeneoVariantRelationshipResolution resolution,
        Func<Task<Product>> upsertChildProductAsync)
    {
        if (upsertChildProductAsync == null)
            throw new ArgumentNullException(nameof(upsertChildProductAsync));

        var product = await upsertChildProductAsync();

        var changed = false;

        // Standalone mode means this Akeneo item should exist as a normal simple product.
        if (product.ProductType != ProductType.SimpleProduct)
        {
            product.ProductType = ProductType.SimpleProduct;
            changed = true;
        }

        // If this product was previously imported as a grouped child, detach it.
        if (product.ParentGroupedProductId != 0)
        {
            product.ParentGroupedProductId = 0;
            changed = true;
        }

        // Standalone products should normally be visible individually.
        // If your import mapping intentionally controls this elsewhere, remove this block.
        if (!product.VisibleIndividually)
        {
            product.VisibleIndividually = true;
            changed = true;
        }

        // Standalone products should not be forced into attribute-combination inventory mode.
        if (product.ManageInventoryMethod == ManageInventoryMethod.ManageStockByAttributes)
        {
            product.ManageInventoryMethod = ManageInventoryMethod.ManageStock;
            changed = true;
        }

        if (changed)
            await productService.UpdateProductAsync(product);

        return new AkeneoVariantImportResult
        {
            Mode = AkeneoVariantRelationshipMode.None,
            Source = resolution.Source,
            FamilyVariantImportConfigurationId = resolution.Options?.FamilyVariantImportConfigurationId,
            NopProductId = product.Id,
            NopProductAttributeCombinationId = null
        };
    }

    private string BuildAssociatedValueName(AkeneoVariantImportContext context)
    {
        var axisValues = context.AxisValuesByAkeneoCode
            .Select(item => (AxisCode: item.Key, DisplayValue: item.Value))
            .ToList();

        var valueName = AkeneoAssociatedValueNameTemplate.Render(
            context.Options?.AssociatedValueNameTemplate,
            axisValues,
            context.Sku,
            context.AkeneoIdentifier,
            attributeCode => productValueResolver.GetValue(
                context.SourceProduct,
                attributeCode,
                context.Locale,
                context.Channel,
                context.Currency));

        if (!string.IsNullOrWhiteSpace(valueName))
            return valueName.Trim();

        return context.Sku
            ?? context.AkeneoIdentifier
            ?? axisValues.FirstOrDefault().DisplayValue
            ?? string.Empty;
    }

    private static string BuildAttributesXml(IEnumerable<ProductAttributeSelection> selections)
    {
        var ordered = selections
            .OrderBy(x => x.ProductAttributeMappingId)
            .ToList();

        var document = new XDocument(
            new XElement("Attributes",
                ordered.Select(selection =>
                    new XElement("ProductAttribute",
                        new XAttribute("ID", selection.ProductAttributeMappingId),
                        new XElement("ProductAttributeValue",
                            new XElement("Value", selection.ProductAttributeValueId))))));

        return document.ToString(SaveOptions.DisableFormatting);
    }

    private class ProductAttributeSelection
    {
        public int ProductAttributeMappingId { get; set; }

        public int ProductAttributeValueId { get; set; }
    }
}

