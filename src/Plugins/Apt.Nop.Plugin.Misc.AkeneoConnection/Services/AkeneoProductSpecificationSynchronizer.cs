using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
public class AkeneoProductSpecificationSynchronizer(ISpecificationAttributeService specificationAttributeService,
    IProductService productService,
    IProductAttributeService productAttributeService,
    IAkeneoNopEntityMappingService entityMappingService)
    : IAkeneoProductSectionSynchronizer
{
    private async Task<bool> ApplySpecificationAttributesAsync(
    Product product,
    IList<AkeneoResolvedMappedValue> mappedValues,
    AkeneoProductImportRequest request,
    AkeneoProductImportResult result)
    {
        var changed = false;

        foreach (var mappedValue in mappedValues.Where(value => value.TargetType == NopTargetType.SpecificationAttribute))
        {
            var specificationAttributeId = mappedValue.Mapping.NopTargetEntityId ?? 0;

            if (specificationAttributeId <= 0)
            {
                result.AddWarning(
                    $"Specification attribute mapping has no nopCommerce specification attribute. Akeneo attribute: {mappedValue.Mapping.AkeneoAttributeCode}");

                continue;
            }

            foreach (var optionItem in GetResolvedOptionItems(mappedValue))
            {
                var option = await GetOrCreateSpecificationAttributeOptionAsync(
                    specificationAttributeId,
                    mappedValue.Mapping.AkeneoAttributeCode,
                    optionItem.AkeneoOptionCode,
                    optionItem.DisplayName,
                    request.CreateMissingSpecificationAttributeOptions);

                if (option == null)
                {
                    result.AddWarning(
                        $"Specification option '{optionItem.DisplayName}' was not found and could not be created. Akeneo attribute: {mappedValue.Mapping.AkeneoAttributeCode}");

                    continue;
                }

                var added = await AddProductSpecificationAttributeIfMissingAsync(
                    product.Id,
                    option.Id);

                if (!added)
                    continue;

                changed = true;
                result.AddMessage($"Assigned specification option '{option.Name}' to product.");
            }
        }

        return changed;
    }

    private async Task<bool> AddProductSpecificationAttributeIfMissingAsync(
        int productId,
        int specificationAttributeOptionId)
    {
        var existingProductSpecificationAttributes =
            await specificationAttributeService.GetProductSpecificationAttributesAsync(
                productId,
                specificationAttributeOptionId: specificationAttributeOptionId);

        if (existingProductSpecificationAttributes.Any(attribute =>
                attribute.SpecificationAttributeOptionId == specificationAttributeOptionId))
        {
            return false;
        }

        await specificationAttributeService.InsertProductSpecificationAttributeAsync(
            new ProductSpecificationAttribute
            {
                ProductId = productId,
                AttributeTypeId = (int)SpecificationAttributeType.Option,
                SpecificationAttributeOptionId = specificationAttributeOptionId,
                AllowFiltering = true,
                ShowOnProductPage = true,
                DisplayOrder = 0
            });

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

        akeneoAttributeCode = akeneoAttributeCode?.Trim();
        akeneoProductKey = akeneoProductKey?.Trim();
        akeneoOptionCode = akeneoOptionCode?.Trim();
        valueName = valueName?.Trim() ?? akeneoOptionCode;

        var akeneoValueMappingCode = BuildProductAttributeValueMappingCode(
            akeneoProductKey,
            akeneoAttributeCode,
            akeneoOptionCode ?? valueName);

        if (!string.IsNullOrWhiteSpace(akeneoValueMappingCode))
        {
            var mappedValueId = await entityMappingService.GetMappedNopEntityIdByAkeneoCodeAsync(
                AkeneoEntityType.Option,
                akeneoValueMappingCode,
                NopEntityType.ProductAttributeValue);

            if (mappedValueId.HasValue)
            {
                var mappedValue = await productAttributeService.GetProductAttributeValueByIdAsync(
                    mappedValueId.Value);

                if (mappedValue != null &&
                    mappedValue.ProductAttributeMappingId == productAttributeMappingId)
                {
                    if (!string.Equals(mappedValue.Name, valueName, StringComparison.Ordinal))
                    {
                        mappedValue.Name = valueName;
                        await productAttributeService.UpdateProductAttributeValueAsync(mappedValue);

                        return new ProductAttributeValueChangeResult
                        {
                            Value = mappedValue,
                            Changed = true
                        };
                    }

                    return new ProductAttributeValueChangeResult
                    {
                        Value = mappedValue,
                        Changed = false
                    };
                }
            }
        }

        var existingValues = await productAttributeService.GetProductAttributeValuesAsync(
            productAttributeMappingId);

        var existingValue = existingValues.FirstOrDefault(value =>
            string.Equals(value.Name, valueName, StringComparison.OrdinalIgnoreCase));

        if (existingValue != null)
        {
            if (!string.IsNullOrWhiteSpace(akeneoValueMappingCode))
            {
                await entityMappingService.UpsertAkeneoNopEntityMappingAsync(
                    AkeneoEntityType.Option,
                    akeneoValueMappingCode,
                    null,
                    NopEntityType.ProductAttributeValue,
                    existingValue.Id);
            }

            return new ProductAttributeValueChangeResult
            {
                Value = existingValue,
                Changed = false
            };
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

        if (!string.IsNullOrWhiteSpace(akeneoValueMappingCode))
        {
            await entityMappingService.UpsertAkeneoNopEntityMappingAsync(
                AkeneoEntityType.Option,
                akeneoValueMappingCode,
                null,
                NopEntityType.ProductAttributeValue,
                newValue.Id);
        }

        return new ProductAttributeValueChangeResult
        {
            Value = newValue,
            Changed = true
        };
    }

    private async Task<SpecificationAttributeOption> GetOrCreateSpecificationAttributeOptionAsync(
      int specificationAttributeId,
      string akeneoAttributeCode,
      string akeneoOptionCode,
      string optionName,
      bool createMissing)
    {
        if (specificationAttributeId <= 0)
            return null;

        if (string.IsNullOrWhiteSpace(optionName) &&
            string.IsNullOrWhiteSpace(akeneoOptionCode))
        {
            return null;
        }

        akeneoAttributeCode = akeneoAttributeCode?.Trim();
        akeneoOptionCode = akeneoOptionCode?.Trim();
        optionName = optionName?.Trim();

        var akeneoOptionMappingCode = BuildAkeneoOptionMappingCode(
            akeneoAttributeCode,
            akeneoOptionCode ?? optionName);

        if (!string.IsNullOrWhiteSpace(akeneoOptionMappingCode))
        {
            var mappedOptionId = await entityMappingService.GetMappedNopEntityIdByAkeneoCodeAsync(
                AkeneoEntityType.Option,
                akeneoOptionMappingCode,
                NopEntityType.SpecificationAttributeOption);

            if (mappedOptionId.HasValue)
            {
                var mappedOption = await specificationAttributeService
                    .GetSpecificationAttributeOptionByIdAsync(mappedOptionId.Value);

                if (mappedOption != null &&
                    mappedOption.SpecificationAttributeId == specificationAttributeId)
                {
                    return mappedOption;
                }
            }
        }

        var existingOptions = await specificationAttributeService
            .GetSpecificationAttributeOptionsBySpecificationAttributeAsync(specificationAttributeId);

        var existingOption = existingOptions.FirstOrDefault(option =>
            string.Equals(option.Name, optionName, StringComparison.OrdinalIgnoreCase));

        if (existingOption != null)
        {
            if (!string.IsNullOrWhiteSpace(akeneoOptionMappingCode))
            {
                await entityMappingService.UpsertAkeneoNopEntityMappingAsync(
                    AkeneoEntityType.Option,
                    akeneoOptionMappingCode,
                    null,
                    NopEntityType.SpecificationAttributeOption,
                    existingOption.Id);
            }

            return existingOption;
        }

        if (!createMissing)
            return null;

        var newOption = new SpecificationAttributeOption
        {
            SpecificationAttributeId = specificationAttributeId,
            Name = optionName ?? akeneoOptionCode,
            DisplayOrder = 0
        };

        await specificationAttributeService.InsertSpecificationAttributeOptionAsync(newOption);

        if (!string.IsNullOrWhiteSpace(akeneoOptionMappingCode))
        {
            await entityMappingService.UpsertAkeneoNopEntityMappingAsync(
                AkeneoEntityType.Option,
                akeneoOptionMappingCode,
                null,
                NopEntityType.SpecificationAttributeOption,
                newOption.Id);
        }

        return newOption;
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

    private static IReadOnlyList<AkeneoResolvedOptionItem> GetResolvedOptionItems(AkeneoResolvedMappedValue mappedValue)
    {
        var value = mappedValue.Value;
        if (value == null)
            return Array.Empty<AkeneoResolvedOptionItem>();

        // Codes and labels must be read in the same source order (no pre-distinct) so the
        // index pairing is valid; dedupe only AFTER pairing.
        var codes = ExtractRawValuesInOrder(value);
        var labels = value.DisplayValues is { Count: > 0 }
            ? value.DisplayValues
            : (string.IsNullOrWhiteSpace(value.DisplayValue)
                ? Array.Empty<string>()
                : new[] { value.DisplayValue });

        var maxCount = Math.Max(codes.Count, labels.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<AkeneoResolvedOptionItem>();

        for (var i = 0; i < maxCount; i++)
        {
            var code = i < codes.Count ? codes[i]?.Trim() : null;
            var label = i < labels.Count ? labels[i]?.Trim() : null;
            var displayName = !string.IsNullOrWhiteSpace(label) ? label : code;

            if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(displayName))
                continue;

            var dedupeKey = code ?? displayName;
            if (!seen.Add(dedupeKey))
                continue;

            items.Add(new AkeneoResolvedOptionItem
            {
                AkeneoOptionCode = code,
                DisplayName = displayName
            });
        }

        return items;
    }

    private static string BuildAkeneoOptionMappingCode(
        string akeneoAttributeCode,
        string akeneoOptionCode)
    {
        if (string.IsNullOrWhiteSpace(akeneoAttributeCode) ||
            string.IsNullOrWhiteSpace(akeneoOptionCode))
        {
            return null;
        }

        return $"{akeneoAttributeCode.Trim()}:{akeneoOptionCode.Trim()}";
    }

    private static IReadOnlyList<string> ExtractRawValuesInOrder(AkeneoResolvedProductValue value)
    {
        if (value?.RawData == null)
            return Array.Empty<string>();

        var rawData = value.RawData.Value;
        if (rawData.ValueKind == JsonValueKind.Array)
        {
            return rawData.EnumerateArray()
                .Select(ConvertJsonElementToString)
                .Select(v => v?.Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();
        }

        var single = ConvertJsonElementToString(rawData)?.Trim();
        return string.IsNullOrWhiteSpace(single) ? Array.Empty<string>() : new[] { single };
    }

    private static string ConvertJsonElementToString(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Object => element.GetRawText(),
            _ => null
        };
    }


    // AkeneoProductSpecificationSynchronizer.cs — replace SynchronizeAsync (line ~412)
    public async Task SynchronizeAsync(
        AkeneoProductSyncContext context,
        CancellationToken cancellationToken = default)
    {
        if (context.Product is null)
            return;

        if (context.Request.SpecificationAttributeSyncMode ==
            AkeneoCollectionSyncMode.Disabled)
        {
            return;
        }

        var mappings = context
            .GetMappings(NopTargetType.SpecificationAttribute)
            .ToList();

        var changed = await ApplySpecificationAttributesAsync(
            context.Product,
            mappings,
            context.Request,
            context.Result);

        if (changed)
            context.MarkChanged();
    }

    public int Order => 400;
}
