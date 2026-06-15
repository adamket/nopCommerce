using System.util;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;

public class AkeneoMappingModelFactory : IAkeneoMappingModelFactory
{

    private readonly IAkeneoApiClient _akeneoApiClient;
    private readonly IAkeneoAttributeMappingService _akeneoAttributeMappingService;
    private readonly IAkeneoNopEntityMappingService _akeneoNopEntityMappingService;
    private readonly ISpecificationAttributeService _specificationAttributeService;
    private readonly IProductAttributeService _productAttributeService;

    public AkeneoMappingModelFactory(
        IAkeneoApiClient akeneoApiClient,
        IAkeneoAttributeMappingService akeneoAttributeMappingService,
        IAkeneoNopEntityMappingService akeneoNopEntityMappingService,
        ISpecificationAttributeService specificationAttributeService,
        IProductAttributeService productAttributeService)
    {
        _akeneoApiClient = akeneoApiClient;
        _akeneoAttributeMappingService = akeneoAttributeMappingService;
        _akeneoNopEntityMappingService = akeneoNopEntityMappingService;
        _specificationAttributeService = specificationAttributeService;
        _productAttributeService = productAttributeService;
    }

    public async Task<AkeneoAttributeMappingListModel> PrepareAttributeMappingListModelAsync()
    {
        return await PrepareAttributeMappingListModelAsync(new AkeneoAttributeMappingListModel());
    }

    public async Task<AkeneoAttributeMappingListModel> PrepareAttributeMappingListModelAsync(
     AkeneoAttributeMappingListModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var akeneoAttributes = await _akeneoApiClient.GetAttributesAsync();

        var existingAttributeMappings =
            await _akeneoAttributeMappingService.GetAllAkeneoAttributeMappingsAsync();

        var existingEntityMappings =
            await _akeneoNopEntityMappingService.GetAkeneoNopEntityMappingsAsync(
                akeneoEntityType: AkeneoEntityType.Attribute);

        var specificationAttributes =
            await _specificationAttributeService.GetAllSpecificationAttributesAsync();

        var productAttributes =
            await _productAttributeService.GetAllProductAttributesAsync();

        model.Mappings.Clear();
        model.Warnings.Clear();

        //PrepareSyncContextOptions(model);

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

            var defaultNopTargetType = GetDefaultNopTargetType(akeneoAttribute);

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
                    ?? (int)GetAkeneoAttributeType(akeneoAttribute),

                NopTargetTypeId = existingAttributeMapping?.NopTargetTypeId
                    ?? (int)defaultNopTargetType,

                NopTargetKey = existingAttributeMapping?.NopTargetKey
                    ?? GetDefaultNopTargetKey(akeneoAttribute, defaultNopTargetType),

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

   

    private static void PrepareTargetTypeOptions(
        AkeneoAttributeMappingModel model,
        AkeneoAttributeDefinition akeneoAttribute)
    {
        var options = GetAllowedTargetTypes(akeneoAttribute)
            .Select(targetType => new SelectListItem
            {
                Text = GetTargetTypeDisplayName(targetType),
                Value = ((int)targetType).ToString(),
                Selected = model.TargetTypeId == (int)targetType
            })
            .ToList();

        model.AvailableTargetTypes.Clear();
        model.AvailableTargetTypes.AddRange(options);
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
    private static NopTargetType GetDefaultNopTargetType(
        AkeneoAttributeDefinition akeneoAttribute)
    {
        var type = akeneoAttribute.Type?.Trim().ToLowerInvariant();

        return type switch
        {
            "pim_catalog_identifier" => NopTargetType.ProductField,
            "pim_catalog_text" => NopTargetType.SpecificationAttribute,
            "pim_catalog_textarea" => NopTargetType.ProductField,
            "pim_catalog_price_collection" => NopTargetType.ProductField,
            "pim_catalog_simpleselect" => NopTargetType.SpecificationAttribute,
            "pim_catalog_multiselect" => NopTargetType.SpecificationAttribute,
            "pim_catalog_boolean" => NopTargetType.SpecificationAttribute,
            "pim_catalog_number" => NopTargetType.SpecificationAttribute,
            "pim_catalog_metric" => NopTargetType.SpecificationAttribute,
            "pim_catalog_date" => NopTargetType.SpecificationAttribute,
            "akeneo_reference_entity" => NopTargetType.SpecificationAttribute,

            _ => NopTargetType.Ignore
        };
    }

    private static AkeneoAttributeType GetAkeneoAttributeType(
        AkeneoAttributeDefinition akeneoAttribute)
    {
        var type = akeneoAttribute.Type?.Trim().ToLowerInvariant();

        return type switch
        {
            "pim_catalog_identifier" => AkeneoAttributeType.Identifier,
            "pim_catalog_text" => AkeneoAttributeType.Text,
            "pim_catalog_textarea" => AkeneoAttributeType.TextArea,
            "pim_catalog_number" => AkeneoAttributeType.Number,
            "pim_catalog_price_collection" => AkeneoAttributeType.Price,
            "pim_catalog_date" => AkeneoAttributeType.Date,
            "pim_catalog_boolean" => AkeneoAttributeType.Boolean,
            "pim_catalog_simpleselect" => AkeneoAttributeType.Select,
            "pim_catalog_multiselect" => AkeneoAttributeType.MultiSelect,
            "pim_catalog_image" => AkeneoAttributeType.Image,
            "pim_catalog_file" => AkeneoAttributeType.File,
            "pim_catalog_metric" => AkeneoAttributeType.Metric,
            "akeneo_reference_entity" => AkeneoAttributeType.ReferenceEntity,

            _ => AkeneoAttributeType.Unknown
        };
    }


    private static IList<NopTargetType> GetAllowedTargetTypes(
        AkeneoAttributeDefinition akeneoAttribute)
    {
        var type = akeneoAttribute.Type?.Trim().ToLowerInvariant();

        return type switch
        {
            "pim_catalog_identifier" => new List<NopTargetType>
        {
            NopTargetType.Ignore,
            NopTargetType.ProductField
        },

            "pim_catalog_text" => new List<NopTargetType>
        {
            NopTargetType.Ignore,
            NopTargetType.ProductField,
            NopTargetType.SpecificationAttribute,
            NopTargetType.ProductAttribute,
            NopTargetType.SeoField,
            NopTargetType.CustomProperty
        },

            "pim_catalog_textarea" => new List<NopTargetType>
        {
            NopTargetType.Ignore,
            NopTargetType.ProductField,
            NopTargetType.SpecificationAttribute,
            NopTargetType.SeoField,
            NopTargetType.CustomProperty
        },

            "pim_catalog_price_collection" => new List<NopTargetType>
        {
            NopTargetType.Ignore,
            NopTargetType.ProductField
        },

            "pim_catalog_simpleselect" => new List<NopTargetType>
        {
            NopTargetType.Ignore,
            NopTargetType.ProductAttribute,
            NopTargetType.SpecificationAttribute,
            NopTargetType.Manufacturer,
            NopTargetType.Category,
            NopTargetType.CustomProperty
        },

            "pim_catalog_multiselect" => new List<NopTargetType>
        {
            NopTargetType.Ignore,
            NopTargetType.ProductAttribute,
            NopTargetType.SpecificationAttribute,
            NopTargetType.Category,
            NopTargetType.CustomProperty
        },

            "pim_catalog_boolean" => new List<NopTargetType>
        {
            NopTargetType.Ignore,
            NopTargetType.ProductField,
            NopTargetType.SpecificationAttribute,
            NopTargetType.CustomProperty
        },

            "pim_catalog_number" or
            "pim_catalog_metric" or
            "pim_catalog_date" => new List<NopTargetType>
            {
            NopTargetType.Ignore,
            NopTargetType.ProductField,
            NopTargetType.SpecificationAttribute,
            NopTargetType.CustomProperty
            },

            "akeneo_reference_entity" => new List<NopTargetType>
        {
            NopTargetType.Ignore,
            NopTargetType.Manufacturer,
            NopTargetType.SpecificationAttribute,
            NopTargetType.CustomProperty
        },

            _ => new List<NopTargetType>
        {
            NopTargetType.Ignore,
            NopTargetType.CustomProperty
        }
        };
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

    private static void PrepareNopTargetKeyOptions(AkeneoAttributeMappingModel model)
    {
        model.AvailableNopTargetKeys.Clear();

        model.AvailableNopTargetKeys.Add(new SelectListItem
        {
            Text = "None",
            Value = ""
        });

        var keys = model.NopTargetTypeId switch
        {
            (int)NopTargetType.ProductField => new Dictionary<string, string>
            {
                ["Name"] = "Name",
                ["ShortDescription"] = "Short description",
                ["FullDescription"] = "Full description",
                ["Sku"] = "SKU",
                ["Price"] = "Price",
                ["Gtin"] = "GTIN",
                ["ManufacturerPartNumber"] = "Manufacturer part number",
                ["Published"] = "Published"
            },

            (int)NopTargetType.SeoField => new Dictionary<string, string>
            {
                ["MetaTitle"] = "Meta title",
                ["MetaDescription"] = "Meta description",
                ["MetaKeywords"] = "Meta keywords",
                ["SeName"] = "Search engine name"
            },

            (int)NopTargetType.CustomProperty => new Dictionary<string, string>
            {
                ["CustomProperty"] = "Custom property"
            },

            _ => new Dictionary<string, string>()
        };

        foreach (var key in keys)
        {
            model.AvailableNopTargetKeys.Add(new SelectListItem
            {
                Text = key.Value,
                Value = key.Key,
                Selected = string.Equals(model.NopTargetKey, key.Key, StringComparison.OrdinalIgnoreCase)
            });
        }
    }

    private static string GetDefaultNopTargetKey(
        AkeneoAttributeDefinition akeneoAttribute,
        NopTargetType nopTargetType)
    {
        if (nopTargetType == NopTargetType.Ignore)
            return string.Empty;

        var code = akeneoAttribute.Code?.Trim().ToLowerInvariant();
        var type = akeneoAttribute.Type?.Trim().ToLowerInvariant();

        if (nopTargetType == NopTargetType.ProductField)
        {
            if (type == "pim_catalog_identifier")
                return "Sku";

            if (type == "pim_catalog_price_collection")
                return "Price";

            return code switch
            {
                "name" or "product_name" => "Name",
                "short_description" or "summary" => "ShortDescription",
                "description" or "full_description" => "FullDescription",
                "sku" => "Sku",
                "gtin" or "barcode" => "Gtin",
                "mpn" or "manufacturer_part_number" => "ManufacturerPartNumber",
                "enabled" or "published" => "Published",
                _ => string.Empty
            };
        }

        if (nopTargetType == NopTargetType.SeoField)
        {
            return code switch
            {
                "meta_title" or "seo_title" => "MetaTitle",
                "meta_description" or "seo_description" => "MetaDescription",
                "meta_keywords" or "seo_keywords" => "MetaKeywords",
                "slug" or "se_name" => "SeName",
                _ => string.Empty
            };
        }

        if (nopTargetType == NopTargetType.CustomProperty)
            return akeneoAttribute.Code;

        return string.Empty;
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