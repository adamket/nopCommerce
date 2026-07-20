using System.Xml.Linq;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public class AkeneoProductAttributeSynchronizer(
    IProductAttributeService productAttributeService,
    IAkeneoNopEntityMappingService entityMappingService,
    IAkeneoManagedRelationService managedRelationService,
    IAkeneoFamilyMappingService familyMappingService)
    : IAkeneoProductSectionSynchronizer
{
    public int Order => 500;

    public async Task SynchronizeAsync(
        AkeneoProductSyncContext context,
        CancellationToken cancellationToken = default)
    {
        if (context.Product is null ||
            context.Request.ProductAttributeSyncMode == AkeneoCollectionSyncMode.Disabled)
        {
            return;
        }

        var mappings = context
            .GetMappings(NopTargetType.ProductAttribute)
            .ToList();

        if (!mappings.Any())
            return;

        var variantAxisCodes = await GetVariantAxisCodesAsync(context.MappingFamilyCode);
        var changed = false;

        foreach (var mapped in mappings)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (variantAxisCodes.Contains(mapped.Mapping.AkeneoAttributeCode ?? string.Empty))
            {
                context.Result.AddMessage(
                    $"Skipped generic product-attribute synchronization for variant axis '{mapped.Mapping.AkeneoAttributeCode}'.");
                continue;
            }

            changed |= await SynchronizeMappingAsync(context, mapped);
        }

        if (changed)
            context.MarkChanged();
    }

    private async Task<bool> SynchronizeMappingAsync(
        AkeneoProductSyncContext context,
        AkeneoResolvedMappedValue mapped)
    {
        var productAttributeId = mapped.Mapping.NopTargetEntityId ?? 0;

        if (productAttributeId <= 0)
        {
            context.Result.AddWarning(
                $"Product attribute mapping has no nopCommerce product attribute. Akeneo attribute: {mapped.Mapping.AkeneoAttributeCode}");
            return false;
        }

        var optionItems = mapped.HasValue
            ? AkeneoSyncValueHelper.GetOptionItems(mapped)
            : Array.Empty<AkeneoResolvedOptionItem>();

        var productMappings = await productAttributeService
            .GetProductAttributeMappingsByProductIdAsync(context.Product.Id);

        var productMapping = productMappings.FirstOrDefault(item =>
            item.ProductAttributeId == productAttributeId);

        var changed = false;

        if (productMapping == null && optionItems.Count > 0)
        {
            productMapping = new ProductAttributeMapping
            {
                ProductId = context.Product.Id,
                ProductAttributeId = productAttributeId,
                AttributeControlTypeId = (int)AttributeControlType.DropdownList,
                IsRequired = mapped.Mapping.IsRequired,
                DisplayOrder = productMappings.Count + 1
            };

            await productAttributeService.InsertProductAttributeMappingAsync(productMapping);
            changed = true;

            if (context.Request.SyncProfileId.HasValue)
            {
                await managedRelationService.UpsertAsync(
                    context.Request.SyncProfileId.Value,
                    context.Request.SyncRunRecordId,
                    context.Product.Id,
                    AkeneoManagedRelationType.ProductAttributeMapping,
                    productMapping.Id,
                    mapped.Mapping.AkeneoAttributeCode);
            }
        }

        if (productMapping == null)
            return false;

        var mappingChanged = false;

        if (productMapping.AttributeControlTypeId !=
            (int)AttributeControlType.DropdownList)
        {
            productMapping.AttributeControlTypeId =
                (int)AttributeControlType.DropdownList;
            mappingChanged = true;
        }

        if (productMapping.IsRequired != mapped.Mapping.IsRequired)
        {
            productMapping.IsRequired = mapped.Mapping.IsRequired;
            mappingChanged = true;
        }

        if (mappingChanged)
        {
            await productAttributeService
                .UpdateProductAttributeMappingAsync(productMapping);
            changed = true;
        }

        var desiredValueIds = new HashSet<int>();
        var desiredStateComplete = true;

        foreach (var item in optionItems)
        {
            var valueResult = await GetOrCreateProductAttributeValueAsync(
                productMapping.Id,
                mapped.Mapping.AkeneoAttributeCode,
                context.ProductKey,
                item.AkeneoOptionCode,
                item.DisplayName,
                context.Request.CreateMissingProductAttributeValues);

            if (valueResult.Value == null)
            {
                desiredStateComplete = false;
                context.Result.AddWarning(
                    $"Product attribute value '{item.DisplayName}' was not found and could not be created. Akeneo attribute: {mapped.Mapping.AkeneoAttributeCode}");
                continue;
            }

            desiredValueIds.Add(valueResult.Value.Id);
            changed |= valueResult.Changed;

            if (valueResult.PluginOwned && context.Request.SyncProfileId.HasValue)
            {
                await managedRelationService.UpsertAsync(
                    context.Request.SyncProfileId.Value,
                    context.Request.SyncRunRecordId,
                    context.Product.Id,
                    AkeneoManagedRelationType.ProductAttributeValue,
                    valueResult.Value.Id,
                    mapped.Mapping.AkeneoAttributeCode,
                    item.AkeneoOptionCode ?? item.DisplayName);
            }
        }

        if (context.Request.ProductAttributeSyncMode == AkeneoCollectionSyncMode.Merge)
            return changed;

        if (!desiredStateComplete)
        {
            context.Result.AddWarning(
                $"Product attribute removal for '{mapped.Mapping.AkeneoAttributeCode}' was skipped because the desired state was incomplete.");
            return changed;
        }

        var currentValues = await productAttributeService
            .GetProductAttributeValuesAsync(productMapping.Id);

        var combinations = await productAttributeService
            .GetAllProductAttributeCombinationsAsync(context.Product.Id);

        if (context.Request.ProductAttributeSyncMode == AkeneoCollectionSyncMode.ReplaceAll)
        {
            foreach (var value in currentValues)
            {
                if (desiredValueIds.Contains(value.Id) ||
                    value.AttributeValueTypeId != (int)AttributeValueType.Simple ||
                    IsValueUsedByCombination(value.Id, combinations))
                {
                    continue;
                }

                await productAttributeService.DeleteProductAttributeValueAsync(value);
                changed = true;
            }

            changed |= await DeleteEmptyManagedMappingAsync(
                context,
                mapped,
                productMapping);
            return changed;
        }

        if (!context.Request.SyncProfileId.HasValue)
            return changed;

        var managedValues = await managedRelationService.GetByProductAsync(
            context.Request.SyncProfileId.Value,
            context.Product.Id,
            AkeneoManagedRelationType.ProductAttributeValue,
            mapped.Mapping.AkeneoAttributeCode);

        foreach (var relation in managedValues)
        {
            if (desiredValueIds.Contains(relation.NopRelationEntityId))
                continue;

            var value = currentValues.FirstOrDefault(item =>
                item.Id == relation.NopRelationEntityId);

            if (value != null)
            {
                if (IsValueUsedByCombination(value.Id, combinations))
                {
                    context.Result.AddWarning(
                        $"Stale product attribute value '{value.Name}' is used by a product attribute combination and was preserved.");
                    continue;
                }

                await productAttributeService.DeleteProductAttributeValueAsync(value);
                changed = true;
            }

            await managedRelationService.DeleteAsync(relation);
        }

        changed |= await DeleteEmptyManagedMappingAsync(
            context,
            mapped,
            productMapping);

        return changed;
    }

    private async Task<bool> DeleteEmptyManagedMappingAsync(
        AkeneoProductSyncContext context,
        AkeneoResolvedMappedValue mapped,
        ProductAttributeMapping productMapping)
    {
        var remainingValues = await productAttributeService
            .GetProductAttributeValuesAsync(productMapping.Id);

        if (remainingValues.Any())
            return false;

        if (context.Request.ProductAttributeSyncMode ==
            AkeneoCollectionSyncMode.ReplaceAll)
        {
            await productAttributeService
                .DeleteProductAttributeMappingAsync(productMapping);
            return true;
        }

        if (!context.Request.SyncProfileId.HasValue)
            return false;

        var relation = await managedRelationService.GetByRelationEntityAsync(
            context.Request.SyncProfileId.Value,
            AkeneoManagedRelationType.ProductAttributeMapping,
            productMapping.Id);

        if (relation == null ||
            !string.Equals(
                relation.AkeneoAttributeCode,
                mapped.Mapping.AkeneoAttributeCode,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        await productAttributeService
            .DeleteProductAttributeMappingAsync(productMapping);
        await managedRelationService.DeleteAsync(relation);
        return true;
    }

    private async Task<ProductAttributeValueChangeResult> GetOrCreateProductAttributeValueAsync(
        int productAttributeMappingId,
        string akeneoAttributeCode,
        string akeneoProductKey,
        string akeneoOptionCode,
        string valueName,
        bool createMissing)
    {
        if (string.IsNullOrWhiteSpace(valueName) &&
            string.IsNullOrWhiteSpace(akeneoOptionCode))
        {
            return new ProductAttributeValueChangeResult();
        }

        valueName = valueName?.Trim() ?? akeneoOptionCode?.Trim();
        var mappingCode = BuildMappingCode(
            akeneoProductKey,
            akeneoAttributeCode,
            akeneoOptionCode ?? valueName);

        var mappedValueId = await entityMappingService
            .GetMappedNopEntityIdByAkeneoCodeAsync(
                AkeneoEntityType.Option,
                mappingCode,
                NopEntityType.ProductAttributeValue);

        if (mappedValueId.HasValue)
        {
            var mappedValue = await productAttributeService
                .GetProductAttributeValueByIdAsync(mappedValueId.Value);

            if (mappedValue != null &&
                mappedValue.ProductAttributeMappingId == productAttributeMappingId)
            {
                var renamed = !string.Equals(
                    mappedValue.Name,
                    valueName,
                    StringComparison.Ordinal);

                if (renamed)
                {
                    mappedValue.Name = valueName;
                    await productAttributeService
                        .UpdateProductAttributeValueAsync(mappedValue);
                }

                return new ProductAttributeValueChangeResult
                {
                    Value = mappedValue,
                    Changed = renamed,
                    PluginOwned = true
                };
            }
        }

        var existingValues = await productAttributeService
            .GetProductAttributeValuesAsync(productAttributeMappingId);

        var existing = existingValues.FirstOrDefault(value =>
            value.AttributeValueTypeId == (int)AttributeValueType.Simple &&
            string.Equals(value.Name, valueName, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            // A matching manually created value is reused, but not claimed as
            // plugin-owned. ReplaceManaged will therefore preserve it.
            return new ProductAttributeValueChangeResult
            {
                Value = existing,
                PluginOwned = false
            };
        }

        if (!createMissing)
            return new ProductAttributeValueChangeResult();

        var created = new ProductAttributeValue
        {
            ProductAttributeMappingId = productAttributeMappingId,
            AttributeValueTypeId = (int)AttributeValueType.Simple,
            Name = valueName,
            DisplayOrder = existingValues.Count + 1
        };

        await productAttributeService.InsertProductAttributeValueAsync(created);

        await entityMappingService.UpsertAkeneoNopEntityMappingAsync(
            AkeneoEntityType.Option,
            mappingCode,
            null,
            NopEntityType.ProductAttributeValue,
            created.Id);

        return new ProductAttributeValueChangeResult
        {
            Value = created,
            Changed = true,
            Created = true,
            PluginOwned = true
        };
    }

    private async Task<HashSet<string>> GetVariantAxisCodesAsync(string familyCode)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(familyCode))
            return result;

        var family = await familyMappingService.GetByFamilyCodeAsync(familyCode);
        if (family == null)
            return result;

        var axes = await familyMappingService.GetAxisMappingsAsync(family.Id);

        foreach (var axis in axes.Where(axis =>
                     !string.IsNullOrWhiteSpace(axis.AkeneoAttributeCode)))
        {
            result.Add(axis.AkeneoAttributeCode.Trim());
        }

        return result;
    }

    private static bool IsValueUsedByCombination(
        int productAttributeValueId,
        IEnumerable<ProductAttributeCombination> combinations)
    {
        foreach (var combination in combinations)
        {
            if (string.IsNullOrWhiteSpace(combination.AttributesXml))
                continue;

            try
            {
                var document = XDocument.Parse(combination.AttributesXml);
                var used = document
                    .Descendants("Value")
                    .Any(element => int.TryParse(element.Value, out var valueId) &&
                                    valueId == productAttributeValueId);

                if (used)
                    return true;
            }
            catch
            {
                // Preserve on malformed XML rather than risk deleting a value
                // that may still be referenced.
                return true;
            }
        }

        return false;
    }

    private static string BuildMappingCode(
        string productKey,
        string attributeCode,
        string optionCode) =>
        $"product-attribute:{productKey?.Trim()}:{attributeCode?.Trim()}:{optionCode?.Trim()}";
}
