using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;

public class AkeneoAttributeAttributeMappingModelFactory(
    IAkeneoApiClient akeneoApiClient,
    IAkeneoAttributeMappingService akeneoAttributeMappingService,
    IAkeneoNopEntityMappingService akeneoNopEntityMappingService,
    ISpecificationAttributeService specificationAttributeService,
    IProductAttributeService productAttributeService,
    IAkeneoTargetTypeResolver targetTypeResolver)
    : IAkeneoAttributeMappingModelFactory
{
    public async Task<AkeneoAttributeMappingListModel> PrepareAttributeMappingListModelAsync()
    {
        return await PrepareAttributeMappingListModelAsync(new AkeneoAttributeMappingListModel());
    }

    public async Task<AkeneoAttributeMappingListModel> PrepareAttributeMappingListModelAsync(
        AkeneoAttributeMappingListModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        IReadOnlyList<AkeneoAttributeDefinition> akeneoAttributes;
        try
        {
            akeneoAttributes = await akeneoApiClient.GetAttributesAsync();
        }
        catch (Exception)
        {
            model.Warnings.Add("Could not load akeneo attributes.  Verify your Akeneo connection and try again.");
            return model;
        }

        var existingAttributeMappings =
            await akeneoAttributeMappingService.GetAllAkeneoAttributeMappingsAsync();

        var existingEntityMappings =
            await akeneoNopEntityMappingService.GetAkeneoNopEntityMappingsAsync(
                akeneoEntityType: AkeneoEntityType.Attribute);

        var specificationAttributes =
            await specificationAttributeService.GetAllSpecificationAttributesAsync();

        var productAttributes =
            await productAttributeService.GetAllProductAttributesAsync();

        model.Mappings.Clear();
        model.Warnings.Clear();

        model.NopTargetKeyMap = BuildNopTargetKeyMap();

        if (!akeneoAttributes.Any())
        {
            model.Warnings.Add("No Akeneo attributes were found. Verify your Akeneo connection and try again.");
            return model;
        }

        foreach (var akeneoAttribute in akeneoAttributes
                     .OrderBy(GetAttributeGroup)
                     .ThenBy(attribute => attribute.Code))
        {
            var existingAttributeMapping = existingAttributeMappings.FirstOrDefault(mapping =>
                string.Equals(mapping.AkeneoAttributeCode, akeneoAttribute.Code, StringComparison.OrdinalIgnoreCase));

            var specificationAttributeMapping = existingEntityMappings.FirstOrDefault(mapping =>
                string.Equals(mapping.AkeneoCode, akeneoAttribute.Code, StringComparison.OrdinalIgnoreCase) &&
                mapping.NopEntityTypeId == (int)NopEntityType.SpecificationAttribute);

            var productAttributeMapping = existingEntityMappings.FirstOrDefault(mapping =>
                string.Equals(mapping.AkeneoCode, akeneoAttribute.Code, StringComparison.OrdinalIgnoreCase) &&
                mapping.NopEntityTypeId == (int)NopEntityType.ProductAttribute);

            var defaultNopTargetType = targetTypeResolver.ResolveDefaultTargetType(akeneoAttribute);

            var mappingModel = new AkeneoAttributeMappingModel
            {
                Id = existingAttributeMapping?.Id ?? 0,

                // Display-only values from Akeneo
                AkeneoAttributeCode = akeneoAttribute.Code,
                AkeneoAttributeLabel = GetAttributeLabel(akeneoAttribute),
                AkeneoAttributeType = akeneoAttribute.Type,
                AkeneoAttributeGroup = GetAttributeGroup(akeneoAttribute),
                AkeneoAttributeGroupLabel = GetAttributeGroupLabel(akeneoAttribute),
                IsLocalizable = akeneoAttribute.Localizable,
                IsScopable = akeneoAttribute.Scopable,

                // Persisted mapping values
                AkeneoAttributeTypeId = existingAttributeMapping?.AkeneoAttributeTypeId
                    ?? (int)targetTypeResolver.ResolveAkeneoAttributeType(akeneoAttribute),

                NopTargetTypeId = existingAttributeMapping?.NopTargetTypeId
                    ?? (int)defaultNopTargetType,

                NopTargetKey = existingAttributeMapping?.NopTargetKey
                    ?? targetTypeResolver.ResolveDefaultTargetKey(akeneoAttribute, defaultNopTargetType),

                Locale = existingAttributeMapping?.Locale,
                Channel = existingAttributeMapping?.Channel,
                TransformRuleJson = existingAttributeMapping?.TransformRuleJson,
                IsRequired = existingAttributeMapping?.IsRequired ?? false,

                // Backed by AkeneoNopEntityMapping
                NopSpecificationAttributeId = specificationAttributeMapping?.NopEntityId,
                NopProductAttributeId = productAttributeMapping?.NopEntityId
            };

            PrepareTargetTypeOptions(mappingModel, akeneoAttribute);
            PrepareNopTargetKeyOptions(mappingModel);
            PrepareSpecificationAttributeOptions(mappingModel, specificationAttributes);
            PrepareProductAttributeOptions(mappingModel, productAttributes);

            model.Mappings.Add(mappingModel);
        }

        AddMappingWarnings(model);

        return model;
    }

    private IDictionary<string, IList<NopTargetKeyOptionModel>> BuildNopTargetKeyMap()
    {
        return targetTypeResolver.TargetKeyOptions.ToDictionary(
            entry => ((int)entry.Key).ToString(),
            entry => (IList<NopTargetKeyOptionModel>)entry.Value
                .Select(option => new NopTargetKeyOptionModel { Value = option.Code, Text = option.Label })
                .ToList());
    }

    private static string GetAttributeLabel(AkeneoAttributeDefinition akeneoAttribute)
    {
        if (akeneoAttribute.Labels != null)
        {
            if (akeneoAttribute.Labels.TryGetValue("en_US", out var englishLabel) &&
                !string.IsNullOrWhiteSpace(englishLabel))
            {
                return englishLabel;
            }

            var firstLabel = akeneoAttribute.Labels.Values.FirstOrDefault(value =>
                !string.IsNullOrWhiteSpace(value));

            if (!string.IsNullOrWhiteSpace(firstLabel))
                return firstLabel;
        }

        return akeneoAttribute.Code;
    }

    private void PrepareTargetTypeOptions(
        AkeneoAttributeMappingModel model,
        AkeneoAttributeDefinition akeneoAttribute)
    {
        model.AvailableTargetTypes.Clear();

        foreach (var targetType in targetTypeResolver.GetAllowedTargetTypes(akeneoAttribute))
        {
            model.AvailableTargetTypes.Add(new SelectListItem
            {
                Text = GetTargetTypeDisplayName(targetType),
                Value = ((int)targetType).ToString(),
                Selected = model.NopTargetTypeId == (int)targetType
            });
        }
    }

    private void PrepareNopTargetKeyOptions(AkeneoAttributeMappingModel model)
    {
        model.AvailableNopTargetKeys.Clear();

        model.AvailableNopTargetKeys.Add(new SelectListItem
        {
            Text = "None",
            Value = ""
        });

        if (!Enum.IsDefined(typeof(NopTargetType), model.NopTargetTypeId))
            return;

        foreach (var option in targetTypeResolver.GetTargetKeyOptions((NopTargetType)model.NopTargetTypeId))
        {
            model.AvailableNopTargetKeys.Add(new SelectListItem
            {
                Text = option.Label,
                Value = option.Code,
                Selected = string.Equals(model.NopTargetKey, option.Code, StringComparison.OrdinalIgnoreCase)
            });
        }
    }

    private static void PrepareSpecificationAttributeOptions(
        AkeneoAttributeMappingModel model,
        IEnumerable<SpecificationAttribute> specificationAttributes)
    {
        model.AvailableSpecificationAttributes.Clear();

        model.AvailableSpecificationAttributes.Add(new SelectListItem
        {
            Text = "None",
            Value = ""
        });

        foreach (var specificationAttribute in specificationAttributes.OrderBy(attribute => attribute.Name))
        {
            model.AvailableSpecificationAttributes.Add(new SelectListItem
            {
                Text = specificationAttribute.Name,
                Value = specificationAttribute.Id.ToString(),
                Selected = model.NopSpecificationAttributeId == specificationAttribute.Id
            });
        }
    }

    private static void PrepareProductAttributeOptions(
        AkeneoAttributeMappingModel model,
        IEnumerable<ProductAttribute> productAttributes)
    {
        model.AvailableProductAttributes.Clear();

        model.AvailableProductAttributes.Add(new SelectListItem
        {
            Text = "None",
            Value = ""
        });

        foreach (var productAttribute in productAttributes.OrderBy(attribute => attribute.Name))
        {
            model.AvailableProductAttributes.Add(new SelectListItem
            {
                Text = productAttribute.Name,
                Value = productAttribute.Id.ToString(),
                Selected = model.NopProductAttributeId == productAttribute.Id
            });
        }
    }

    private static void AddMappingWarnings(AkeneoAttributeMappingListModel model)
    {
        var activeMappings = model.Mappings
            .Where(mapping => mapping.NopTargetTypeId != (int)NopTargetType.Ignore)
            .ToList();

        if (!activeMappings.Any())
        {
            model.Warnings.Add("No attribute mappings are currently active.");
            return;
        }

        if (!activeMappings.Any(mapping =>
                mapping.NopTargetTypeId == (int)NopTargetType.ProductField &&
                string.Equals(mapping.NopTargetKey, "Sku", StringComparison.OrdinalIgnoreCase)))
        {
            model.Warnings.Add("No Akeneo attribute is mapped to Product SKU.");
        }

        if (!activeMappings.Any(mapping =>
                mapping.NopTargetTypeId == (int)NopTargetType.ProductField &&
                string.Equals(mapping.NopTargetKey, "Name", StringComparison.OrdinalIgnoreCase)))
        {
            model.Warnings.Add("No Akeneo attribute is mapped to Product Name.");
        }

        if (!activeMappings.Any(mapping =>
                mapping.NopTargetTypeId == (int)NopTargetType.ProductField &&
                string.Equals(mapping.NopTargetKey, "Price", StringComparison.OrdinalIgnoreCase)))
        {
            model.Warnings.Add("No Akeneo attribute is mapped to Product Price.");
        }

        foreach (var mapping in activeMappings)
        {
            if (mapping.NopTargetTypeId == (int)NopTargetType.SpecificationAttribute &&
                !mapping.NopSpecificationAttributeId.HasValue)
            {
                model.Warnings.Add(
                    $"Akeneo attribute \"{mapping.AkeneoAttributeCode}\" is mapped as a specification attribute, but no nopCommerce specification attribute is selected.");
            }

            if (mapping.NopTargetTypeId == (int)NopTargetType.ProductAttribute &&
                !mapping.NopProductAttributeId.HasValue)
            {
                model.Warnings.Add(
                    $"Akeneo attribute \"{mapping.AkeneoAttributeCode}\" is mapped as a product attribute, but no nopCommerce product attribute is selected.");
            }

            if (mapping.NopTargetTypeId == (int)NopTargetType.ProductField &&
                string.IsNullOrWhiteSpace(mapping.NopTargetKey))
            {
                model.Warnings.Add(
                    $"Akeneo attribute \"{mapping.AkeneoAttributeCode}\" is mapped as a product field, but no target key is selected.");
            }
        }
    }

    private static string GetTargetTypeDisplayName(NopTargetType targetType)
    {
        return targetType switch
        {
            NopTargetType.Ignore => "Ignore",
            NopTargetType.ProductField => "Product Field",
            NopTargetType.ProductAttribute => "Product Attribute",
            NopTargetType.SpecificationAttribute => "Specification Attribute",
            NopTargetType.Manufacturer => "Manufacturer",
            NopTargetType.Category => "Category",
            NopTargetType.SeoField => "SEO Field",
            NopTargetType.CustomProperty => "Custom Property",
            _ => targetType.ToString()
        };
    }

    private static string GetAttributeGroup(AkeneoAttributeDefinition akeneoAttribute)
    {
        return string.IsNullOrWhiteSpace(akeneoAttribute.Group)
            ? "ungrouped"
            : akeneoAttribute.Group;
    }

    private static string GetAttributeGroupLabel(AkeneoAttributeDefinition akeneoAttribute)
    {
        var group = GetAttributeGroup(akeneoAttribute);

        if (string.Equals(group, "ungrouped", StringComparison.OrdinalIgnoreCase))
            return "Ungrouped";

        return group
            .Replace("_", " ")
            .Replace("-", " ");
    }
}