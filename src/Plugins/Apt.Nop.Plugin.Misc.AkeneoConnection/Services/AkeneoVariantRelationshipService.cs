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
    IAkeneoProductValueResolver productValueResolver,
    IAkeneoNopEntityMappingService entityMappingService,
    IAkeneoManagedRelationService managedRelationService)
    : IAkeneoVariantRelationshipService
{
    public async Task<AkeneoVariantSyncResult> ApplyAsync(
        Product parentProduct,
        AkeneoVariantImportContext context,
        Func<Task<Product>> upsertChildProductAsync)
    {
        ArgumentNullException.ThrowIfNull(parentProduct);
        ArgumentNullException.ThrowIfNull(context);

        var resolution = await relationshipResolver.ResolveAsync(
            parentProduct,
            context.AkeneoFamilyCode);

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

        var parentChanged = await EnsureParentShapeAsync(parentProduct, effectiveMode);

        var result = effectiveMode switch
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
                $"Unsupported Akeneo variant relationship mode: {effectiveMode}")
        };

        result.ParentChanged = parentChanged;
        result.NopParentProductId ??= parentProduct.Id;
        return result;
    }

    private async Task<bool> EnsureParentShapeAsync(
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
        else if (parentProduct.ProductType != ProductType.SimpleProduct)
        {
            parentProduct.ProductType = ProductType.SimpleProduct;
            changed = true;
        }

        if (mode == AkeneoVariantRelationshipMode.ProductAttributeCombinations &&
            parentProduct.ManageInventoryMethod != ManageInventoryMethod.ManageStockByAttributes)
        {
            parentProduct.ManageInventoryMethod = ManageInventoryMethod.ManageStockByAttributes;
            changed = true;
        }
        else if (mode != AkeneoVariantRelationshipMode.ProductAttributeCombinations &&
                 parentProduct.ManageInventoryMethod == ManageInventoryMethod.ManageStockByAttributes)
        {
            // Attribute-combination inventory is representation-specific. Do
            // not leave the parent in an invalid stock mode after switching to
            // grouped, associated-product, or standalone representation.
            parentProduct.ManageInventoryMethod = ManageInventoryMethod.DontManageStock;
            changed = true;
        }

        if (changed)
        {
            parentProduct.UpdatedOnUtc = DateTime.UtcNow;
            await productService.UpdateProductAsync(parentProduct);
        }

        return changed;
    }

    private async Task<AkeneoVariantSyncResult> ApplyGroupedProductAsync(
        Product parentProduct,
        AkeneoVariantImportContext context,
        AkeneoVariantRelationshipSource source,
        Func<Task<Product>> upsertChildProductAsync)
    {
        var childProduct = await RequireChildProductAsync(
            context,
            upsertChildProductAsync);

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
        {
            childProduct.UpdatedOnUtc = DateTime.UtcNow;
            await productService.UpdateProductAsync(childProduct);
        }

        if (context.SyncProfileId.HasValue)
        {
            await managedRelationService.UpsertAsync(
                context.SyncProfileId.Value,
                context.SyncRunRecordId,
                childProduct.Id,
                AkeneoManagedRelationType.GroupedProductRelationship,
                childProduct.Id,
                akeneoValueCode: parentProduct.Id.ToString());
        }

        return new AkeneoVariantSyncResult
        {
            Mode = AkeneoVariantRelationshipMode.GroupedProducts,
            Source = source,
            NopProductId = childProduct.Id,
            NopParentProductId = parentProduct.Id,
            RelationshipChanged = changed
        };
    }

    private async Task<AkeneoVariantSyncResult> ApplyAssociatedToProductAttributeValueAsync(
        Product parentProduct,
        AkeneoVariantImportContext context,
        AkeneoVariantRelationshipSource source,
        Func<Task<Product>> upsertChildProductAsync)
    {
        if (!context.Options.AssociatedProductAttributeId.HasValue)
        {
            throw new NopException(
                "Associated-to-product variant synchronization requires AssociatedProductAttributeId.");
        }

        var childProduct = await RequireChildProductAsync(
            context,
            upsertChildProductAsync);

        var changed = false;

        var desiredVisibility = !context.Options.HideChildProductsWhenRepresentedByParent;
        if (childProduct.VisibleIndividually != desiredVisibility)
        {
            childProduct.VisibleIndividually = desiredVisibility;
            childProduct.UpdatedOnUtc = DateTime.UtcNow;
            await productService.UpdateProductAsync(childProduct);
            changed = true;
        }

        var mappingResult = await EnsureProductAttributeMappingAsync(
            parentProduct.Id,
            context.Options.AssociatedProductAttributeId.Value,
            isRequired: true);

        changed |= mappingResult.Changed;

        if (context.SyncProfileId.HasValue)
        {
            await managedRelationService.UpsertAsync(
                context.SyncProfileId.Value,
                context.SyncRunRecordId,
                parentProduct.Id,
                AkeneoManagedRelationType.ProductAttributeMapping,
                mappingResult.Mapping.Id,
                akeneoAttributeCode: "__associated_product");
        }

        var values = await productAttributeService
            .GetProductAttributeValuesAsync(mappingResult.Mapping.Id);

        var valueName = BuildAssociatedValueName(context);
        ProductAttributeValue existingValue = null;

        if (context.ExistingAssociatedProductAttributeValueId.HasValue)
        {
            existingValue = await productAttributeService.GetProductAttributeValueByIdAsync(
                context.ExistingAssociatedProductAttributeValueId.Value);

            if (existingValue?.ProductAttributeMappingId != mappingResult.Mapping.Id)
                existingValue = null;
        }

        existingValue ??= values.FirstOrDefault(value =>
            value.AttributeValueTypeId == (int)AttributeValueType.AssociatedToProduct &&
            value.AssociatedProductId == childProduct.Id);

        if (existingValue == null)
        {
            existingValue = new ProductAttributeValue
            {
                ProductAttributeMappingId = mappingResult.Mapping.Id,
                AttributeValueTypeId = (int)AttributeValueType.AssociatedToProduct,
                AssociatedProductId = childProduct.Id,
                Name = valueName,
                DisplayOrder = values.Count + 1
            };

            await productAttributeService.InsertProductAttributeValueAsync(existingValue);
            changed = true;
        }
        else
        {
            var valueChanged = false;

            if (existingValue.AssociatedProductId != childProduct.Id)
            {
                existingValue.AssociatedProductId = childProduct.Id;
                valueChanged = true;
            }

            if (!string.Equals(existingValue.Name, valueName, StringComparison.Ordinal))
            {
                existingValue.Name = valueName;
                valueChanged = true;
            }

            if (valueChanged)
            {
                await productAttributeService.UpdateProductAttributeValueAsync(existingValue);
                changed = true;
            }
        }

        if (context.SyncProfileId.HasValue)
        {
            await managedRelationService.UpsertAsync(
                context.SyncProfileId.Value,
                context.SyncRunRecordId,
                parentProduct.Id,
                AkeneoManagedRelationType.AssociatedProductAttributeValue,
                existingValue.Id,
                akeneoValueCode: context.AkeneoUuid ?? context.AkeneoIdentifier);
        }

        return new AkeneoVariantSyncResult
        {
            Mode = AkeneoVariantRelationshipMode.AssociatedToProductAttributeValue,
            Source = source,
            NopProductId = childProduct.Id,
            NopParentProductId = parentProduct.Id,
            NopProductAttributeValueId = existingValue.Id,
            RelationshipChanged = changed
        };
    }

    private async Task<AkeneoVariantSyncResult> ApplyProductAttributeCombinationAsync(
        Product parentProduct,
        AkeneoVariantImportContext context,
        AkeneoVariantRelationshipSource source)
    {
        if (context.Options.AxisMappings.Count == 0)
        {
            throw new NopException(
                "Product attribute combination synchronization requires at least one axis mapping.");
        }

        var selections = new List<ProductAttributeSelection>();
        var changed = false;

        foreach (var axis in context.Options.AxisMappings)
        {
            if (!context.AxisValuesByAkeneoCode.TryGetValue(
                    axis.AkeneoAttributeCode,
                    out var axisValue) ||
                string.IsNullOrWhiteSpace(axisValue.DisplayName))
            {
                if (axis.IsRequired)
                {
                    throw new NopException(
                        $"Akeneo variant is missing required axis value '{axis.AkeneoAttributeCode}'. SKU: {context.Sku}");
                }

                continue;
            }

            var mappingResult = await EnsureProductAttributeMappingAsync(
                parentProduct.Id,
                axis.NopProductAttributeId,
                axis.IsRequired);

            changed |= mappingResult.Changed;

            var valueResult = await EnsureAxisValueAsync(
                parentProduct.Id,
                mappingResult.Mapping.Id,
                axisValue);

            changed |= valueResult.Changed;

            selections.Add(new ProductAttributeSelection
            {
                ProductAttributeMappingId = mappingResult.Mapping.Id,
                ProductAttributeValueId = valueResult.Value.Id
            });

            if (context.SyncProfileId.HasValue)
            {
                await managedRelationService.UpsertAsync(
                    context.SyncProfileId.Value,
                    context.SyncRunRecordId,
                    parentProduct.Id,
                    AkeneoManagedRelationType.ProductAttributeMapping,
                    mappingResult.Mapping.Id,
                    axis.AkeneoAttributeCode);

                await managedRelationService.UpsertAsync(
                    context.SyncProfileId.Value,
                    context.SyncRunRecordId,
                    parentProduct.Id,
                    AkeneoManagedRelationType.ProductAttributeValue,
                    valueResult.Value.Id,
                    axis.AkeneoAttributeCode,
                    axisValue.AkeneoOptionCode ?? axisValue.DisplayName);
            }
        }

        if (selections.Count == 0)
        {
            throw new NopException(
                $"No product attribute selections were created for Akeneo variant SKU '{context.Sku}'.");
        }

        var attributesXml = BuildAttributesXml(selections);
        var combinations = await productAttributeService
            .GetAllProductAttributeCombinationsAsync(parentProduct.Id);

        ProductAttributeCombination combination = null;

        if (context.ExistingProductAttributeCombinationId.HasValue)
        {
            combination = await productAttributeService
                .GetProductAttributeCombinationByIdAsync(
                    context.ExistingProductAttributeCombinationId.Value);

            if (combination?.ProductId != parentProduct.Id)
                combination = null;
        }

        combination ??= combinations.FirstOrDefault(item =>
            string.Equals(item.AttributesXml, attributesXml, StringComparison.OrdinalIgnoreCase));

        var combinationCreated = combination == null;

        if (combinationCreated)
        {
            combination = new ProductAttributeCombination
            {
                ProductId = parentProduct.Id,
                AttributesXml = attributesXml,
                Sku = context.Sku,
                StockQuantity = context.StockQuantity ?? 0,
                OverriddenPrice = context.Price
            };

            await productAttributeService.InsertProductAttributeCombinationAsync(combination);
            changed = true;
        }
        else
        {
            var combinationChanged = false;

            if (!string.Equals(combination.AttributesXml, attributesXml, StringComparison.Ordinal))
            {
                combination.AttributesXml = attributesXml;
                combinationChanged = true;
            }

            if (!string.Equals(combination.Sku, context.Sku, StringComparison.Ordinal))
            {
                combination.Sku = context.Sku;
                combinationChanged = true;
            }

            if (context.StockQuantity.HasValue &&
                combination.StockQuantity != context.StockQuantity.Value)
            {
                combination.StockQuantity = context.StockQuantity.Value;
                combinationChanged = true;
            }

            if (context.Price.HasValue &&
                combination.OverriddenPrice != context.Price.Value)
            {
                combination.OverriddenPrice = context.Price.Value;
                combinationChanged = true;
            }

            if (combinationChanged)
            {
                await productAttributeService
                    .UpdateProductAttributeCombinationAsync(combination);
                changed = true;
            }
        }

        if (context.SyncProfileId.HasValue)
        {
            await managedRelationService.UpsertAsync(
                context.SyncProfileId.Value,
                context.SyncRunRecordId,
                parentProduct.Id,
                AkeneoManagedRelationType.ProductAttributeCombination,
                combination.Id,
                akeneoValueCode: context.AkeneoUuid ?? context.AkeneoIdentifier);
        }

        return new AkeneoVariantSyncResult
        {
            Mode = AkeneoVariantRelationshipMode.ProductAttributeCombinations,
            Source = source,
            NopProductId = parentProduct.Id,
            NopParentProductId = parentProduct.Id,
            NopProductAttributeCombinationId = combination.Id,
            DestinationChanged = changed,
            DestinationCreated = combinationCreated
        };
    }

    private async Task<ProductAttributeMappingResult> EnsureProductAttributeMappingAsync(
        int productId,
        int productAttributeId,
        bool isRequired)
    {
        var mappings = await productAttributeService
            .GetProductAttributeMappingsByProductIdAsync(productId);

        var existing = mappings.FirstOrDefault(item =>
            item.ProductAttributeId == productAttributeId);

        if (existing != null)
        {
            var changed = false;

            if (existing.AttributeControlTypeId != (int)AttributeControlType.DropdownList)
            {
                existing.AttributeControlTypeId = (int)AttributeControlType.DropdownList;
                changed = true;
            }

            if (existing.IsRequired != isRequired)
            {
                existing.IsRequired = isRequired;
                changed = true;
            }

            if (changed)
                await productAttributeService.UpdateProductAttributeMappingAsync(existing);

            return new ProductAttributeMappingResult(existing, changed);
        }

        var mapping = new ProductAttributeMapping
        {
            ProductId = productId,
            ProductAttributeId = productAttributeId,
            AttributeControlTypeId = (int)AttributeControlType.DropdownList,
            IsRequired = isRequired,
            DisplayOrder = mappings.Count + 1
        };

        await productAttributeService.InsertProductAttributeMappingAsync(mapping);
        return new ProductAttributeMappingResult(mapping, true);
    }

    private async Task<AxisValueResult> EnsureAxisValueAsync(
        int parentProductId,
        int productAttributeMappingId,
        AkeneoVariantAxisValue axisValue)
    {
        var optionIdentity = axisValue.AkeneoOptionCode ?? axisValue.DisplayName;
        var mappingCode =
            $"variant-axis:{parentProductId}:{axisValue.AkeneoAttributeCode}:{optionIdentity}";

        var mappedValueId = await entityMappingService
            .GetMappedNopEntityIdByAkeneoCodeAsync(
                AkeneoEntityType.Option,
                mappingCode,
                NopEntityType.ProductAttributeValue);

        if (mappedValueId.HasValue)
        {
            var mapped = await productAttributeService
                .GetProductAttributeValueByIdAsync(mappedValueId.Value);

            if (mapped != null &&
                mapped.ProductAttributeMappingId == productAttributeMappingId)
            {
                if (!string.Equals(mapped.Name, axisValue.DisplayName, StringComparison.Ordinal))
                {
                    mapped.Name = axisValue.DisplayName;
                    await productAttributeService.UpdateProductAttributeValueAsync(mapped);
                    return new AxisValueResult(mapped, true);
                }

                return new AxisValueResult(mapped, false);
            }
        }

        var values = await productAttributeService
            .GetProductAttributeValuesAsync(productAttributeMappingId);

        var existing = values.FirstOrDefault(value =>
            value.AttributeValueTypeId == (int)AttributeValueType.Simple &&
            string.Equals(value.Name, axisValue.DisplayName, StringComparison.OrdinalIgnoreCase));

        if (existing == null)
        {
            existing = new ProductAttributeValue
            {
                ProductAttributeMappingId = productAttributeMappingId,
                AttributeValueTypeId = (int)AttributeValueType.Simple,
                Name = axisValue.DisplayName,
                DisplayOrder = values.Count + 1
            };

            await productAttributeService.InsertProductAttributeValueAsync(existing);
        }

        await entityMappingService.UpsertAkeneoNopEntityMappingAsync(
            AkeneoEntityType.Option,
            mappingCode,
            null,
            NopEntityType.ProductAttributeValue,
            existing.Id);

        return new AxisValueResult(existing, true);
    }

    private async Task<AkeneoVariantSyncResult> ApplyStandaloneProductAsync(
        AkeneoVariantImportContext context,
        AkeneoVariantRelationshipResolution resolution,
        Func<Task<Product>> upsertChildProductAsync)
    {
        var product = await RequireChildProductAsync(
            context,
            upsertChildProductAsync);

        var changed = false;

        if (product.ProductType != ProductType.SimpleProduct)
        {
            product.ProductType = ProductType.SimpleProduct;
            changed = true;
        }

        if (product.ParentGroupedProductId != 0)
        {
            product.ParentGroupedProductId = 0;
            changed = true;
        }

        if (!product.VisibleIndividually)
        {
            product.VisibleIndividually = true;
            changed = true;
        }

        if (product.ManageInventoryMethod == ManageInventoryMethod.ManageStockByAttributes)
        {
            product.ManageInventoryMethod = ManageInventoryMethod.ManageStock;
            changed = true;
        }

        if (changed)
        {
            product.UpdatedOnUtc = DateTime.UtcNow;
            await productService.UpdateProductAsync(product);
        }

        return new AkeneoVariantSyncResult
        {
            Mode = AkeneoVariantRelationshipMode.None,
            Source = resolution.Source,
            FamilyVariantImportConfigurationId = resolution.Options?.FamilyVariantImportConfigurationId,
            NopProductId = product.Id,
            DestinationChanged = changed
        };
    }

    private static async Task<Product> RequireChildProductAsync(
        AkeneoVariantImportContext context,
        Func<Task<Product>> upsertChildProductAsync)
    {
        if (upsertChildProductAsync == null)
            throw new ArgumentNullException(nameof(upsertChildProductAsync));

        var product = await upsertChildProductAsync();

        if (product == null)
        {
            throw new NopException(
                $"Akeneo variant '{context.AkeneoUuid ?? context.AkeneoIdentifier}' could not be synchronized as a child product.");
        }

        return product;
    }

    private string BuildAssociatedValueName(AkeneoVariantImportContext context)
    {
        var axisValues = context.AxisValuesByAkeneoCode.Values
            .Select(item => (
                AxisCode: item.AkeneoAttributeCode,
                DisplayValue: item.DisplayName))
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

    private static string BuildAttributesXml(
        IEnumerable<ProductAttributeSelection> selections)
    {
        var ordered = selections
            .OrderBy(item => item.ProductAttributeMappingId)
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

    private sealed class ProductAttributeSelection
    {
        public int ProductAttributeMappingId { get; init; }

        public int ProductAttributeValueId { get; init; }
    }

    private sealed record ProductAttributeMappingResult(
        ProductAttributeMapping Mapping,
        bool Changed);

    private sealed record AxisValueResult(
        ProductAttributeValue Value,
        bool Changed);
}
