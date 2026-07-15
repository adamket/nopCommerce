using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public class AkeneoProductAttributeSynchronizer(
    IProductAttributeService productAttributeService,
    IAkeneoNopEntityMappingService entityMappingService)
    : IAkeneoProductSectionSynchronizer
{
    public int Order => 500;

    public async Task SynchronizeAsync(
        AkeneoProductSyncContext context,
        CancellationToken cancellationToken = default)
    {
        if (context.Product is null)
            return;

        if (context.Request.ProductAttributeSyncMode ==
            AkeneoCollectionSyncMode.Disabled)
        {
            return;
        }

        var mappings = context
            .GetMappings(NopTargetType.ProductAttribute)
            .ToList();

        if (mappings.Count == 0)
            return;

        var changed = false;
        var replaceMode = context.Request.ProductAttributeSyncMode ==
                          AkeneoCollectionSyncMode.Replace;

        foreach (var mapped in mappings)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var productAttributeId = mapped.Mapping.NopTargetEntityId ?? 0;

            if (productAttributeId <= 0)
            {
                context.Result.AddWarning(
                    $"Product attribute mapping has no nopCommerce product attribute. Akeneo attribute: {mapped.Mapping.AkeneoAttributeCode}");
                continue;
            }

            var optionItems = mapped.HasValue
                ? AkeneoSyncValueHelper.GetOptionItems(mapped)
                : Array.Empty<AkeneoResolvedOptionItem>();

            // Missing value + Merge => leave everything alone (PreserveExisting semantics).
            if (optionItems.Count == 0 && !replaceMode)
                continue;

            var productMappings = await productAttributeService
                .GetProductAttributeMappingsByProductIdAsync(context.Product.Id);

            var productMapping = productMappings.FirstOrDefault(x =>
                x.ProductAttributeId == productAttributeId);

            if (productMapping == null)
            {
                if (optionItems.Count == 0)
                    continue;

                productMapping = new ProductAttributeMapping
                {
                    ProductId = context.Product.Id,
                    ProductAttributeId = productAttributeId,
                    AttributeControlTypeId = (int)AttributeControlType.DropdownList,
                    IsRequired = mapped.Mapping.IsRequired,
                    DisplayOrder = productMappings.Count + 1
                };

                await productAttributeService.InsertProductAttributeMappingAsync(productMapping);

                context.Result.AddMessage(
                    $"Added product attribute mapping (attribute ID {productAttributeId}) to product ID {context.Product.Id}.");
                changed = true;
            }

            var desiredValueIds = new HashSet<int>();

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
                    context.Result.AddWarning(
                        $"Product attribute value '{item.DisplayName}' was not found and could not be created. Akeneo attribute: {mapped.Mapping.AkeneoAttributeCode}");
                    continue;
                }

                desiredValueIds.Add(valueResult.Value.Id);
                changed |= valueResult.Changed;
            }

            if (!replaceMode)
                continue;

            // Replace mode: remove stale values, but ONLY plugin-owned Simple values
            // for THIS product+attribute. Never touch AssociatedToProduct values —
            // the variant relationship service owns those.
            var currentValues = await productAttributeService
                .GetProductAttributeValuesAsync(productMapping.Id);

            var ownershipPrefix =
                $"{context.ProductKey}:{mapped.Mapping.AkeneoAttributeCode}:";

            foreach (var value in currentValues)
            {
                if (desiredValueIds.Contains(value.Id))
                    continue;

                if (value.AttributeValueTypeId != (int)AttributeValueType.Simple)
                    continue;

                var ownerMappings = await entityMappingService.GetMappingsByNopEntityAsync(
                    NopEntityType.ProductAttributeValue,
                    value.Id);

                var pluginOwned = ownerMappings.Any(m =>
                    m.AkeneoEntityTypeId == (int)AkeneoEntityType.Option &&
                    m.AkeneoCode?.StartsWith(ownershipPrefix, StringComparison.OrdinalIgnoreCase) == true);

                if (!pluginOwned)
                    continue;

                await productAttributeService.DeleteProductAttributeValueAsync(value);
                await entityMappingService.DeleteMappingsByNopEntityAsync(
                    NopEntityType.ProductAttributeValue, value.Id);

                context.Result.AddMessage(
                    $"Removed stale product attribute value '{value.Name}' (Replace mode).");
                changed = true;
            }
        }

        if (changed)
            context.MarkChanged();
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

        akeneoAttributeCode = akeneoAttributeCode?.Trim();
        akeneoProductKey = akeneoProductKey?.Trim();
        akeneoOptionCode = akeneoOptionCode?.Trim();
        valueName = valueName?.Trim() ?? akeneoOptionCode;

        var mappingCode = BuildProductAttributeValueMappingCode(
            akeneoProductKey, akeneoAttributeCode, akeneoOptionCode ?? valueName);

        if (!string.IsNullOrWhiteSpace(mappingCode))
        {
            var mappedValueId = await entityMappingService.GetMappedNopEntityIdByAkeneoCodeAsync(
                AkeneoEntityType.Option, mappingCode, NopEntityType.ProductAttributeValue);

            if (mappedValueId.HasValue)
            {
                var mappedValue = await productAttributeService
                    .GetProductAttributeValueByIdAsync(mappedValueId.Value);

                if (mappedValue != null &&
                    mappedValue.ProductAttributeMappingId == productAttributeMappingId)
                {
                    if (!string.Equals(mappedValue.Name, valueName, StringComparison.Ordinal))
                    {
                        mappedValue.Name = valueName;
                        await productAttributeService.UpdateProductAttributeValueAsync(mappedValue);
                        return new ProductAttributeValueChangeResult { Value = mappedValue, Changed = true };
                    }

                    return new ProductAttributeValueChangeResult { Value = mappedValue, Changed = false };
                }
            }
        }

        var existingValues = await productAttributeService
            .GetProductAttributeValuesAsync(productAttributeMappingId);

        var existingValue = existingValues.FirstOrDefault(value =>
            string.Equals(value.Name, valueName, StringComparison.OrdinalIgnoreCase));

        if (existingValue != null)
        {
            if (!string.IsNullOrWhiteSpace(mappingCode))
            {
                await entityMappingService.UpsertAkeneoNopEntityMappingAsync(
                    AkeneoEntityType.Option, mappingCode, null,
                    NopEntityType.ProductAttributeValue, existingValue.Id);
            }

            return new ProductAttributeValueChangeResult { Value = existingValue, Changed = false };
        }

        if (!createMissing)
            return new ProductAttributeValueChangeResult();

        var newValue = new ProductAttributeValue
        {
            ProductAttributeMappingId = productAttributeMappingId,
            AttributeValueTypeId = (int)AttributeValueType.Simple,
            Name = valueName,
            DisplayOrder = 0
        };

        await productAttributeService.InsertProductAttributeValueAsync(newValue);

        if (!string.IsNullOrWhiteSpace(mappingCode))
        {
            await entityMappingService.UpsertAkeneoNopEntityMappingAsync(
                AkeneoEntityType.Option, mappingCode, null,
                NopEntityType.ProductAttributeValue, newValue.Id);
        }

        return new ProductAttributeValueChangeResult { Value = newValue, Changed = true };
    }

    private static string BuildProductAttributeValueMappingCode(
        string akeneoProductKey,
        string akeneoAttributeCode,
        string akeneoOptionCodeOrValue)
    {
        if (string.IsNullOrWhiteSpace(akeneoProductKey) ||
            string.IsNullOrWhiteSpace(akeneoAttributeCode) ||
            string.IsNullOrWhiteSpace(akeneoOptionCodeOrValue))
        {
            return null;
        }

        return $"{akeneoProductKey.Trim()}:{akeneoAttributeCode.Trim()}:{akeneoOptionCodeOrValue.Trim()}";
    }
}