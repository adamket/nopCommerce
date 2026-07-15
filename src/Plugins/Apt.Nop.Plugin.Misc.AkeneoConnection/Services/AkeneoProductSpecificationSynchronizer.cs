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

            foreach (var optionItem in AkeneoSyncValueHelper.GetOptionItems(mappedValue))
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
