using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public sealed class AkeneoTargetTypeResolver : IAkeneoTargetTypeResolver
{
    private static readonly IReadOnlyDictionary<NopTargetType, IReadOnlyList<NopTargetKeyOption>> _targetKeyOptions =
        new Dictionary<NopTargetType, IReadOnlyList<NopTargetKeyOption>>
        {
            [NopTargetType.ProductField] =
            [
                new("Name", "Name"),
                new("ShortDescription", "Short description"),
                new("FullDescription", "Full description"),
                new("Sku", "SKU"),
                new("Price", "Price"),
                new("StockQuantity", "Stock quantity"),
                new("Gtin", "GTIN"),
                new("ManufacturerPartNumber", "Manufacturer part number"),
                new("Published", "Published")
            ],

            [NopTargetType.SeoField] =
            [
                new("MetaTitle", "Meta title"),
                new("MetaDescription", "Meta description"),
                new("MetaKeywords", "Meta keywords"),
                new("SeName", "Search engine name")
            ],

            [NopTargetType.CustomProperty] =
            [
                new("CustomProperty", "Custom property")
            ]
        };

    public IReadOnlyDictionary<NopTargetType, IReadOnlyList<NopTargetKeyOption>> TargetKeyOptions => _targetKeyOptions;

    public IReadOnlyList<NopTargetKeyOption> GetTargetKeyOptions(NopTargetType targetType) =>
        _targetKeyOptions.TryGetValue(targetType, out var options)
            ? options
            : [];

    public AkeneoAttributeType ResolveAkeneoAttributeType(AkeneoAttributeDefinition attribute)
    {
        return Normalize(attribute.Type) switch
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
            "akeneo_reference_entity_collection" => AkeneoAttributeType.ReferenceEntityCollection,
            _ => AkeneoAttributeType.Unknown
        };
    }

    public NopTargetType ResolveDefaultTargetType(AkeneoAttributeDefinition attribute)
    {
        return Normalize(attribute.Type) switch
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
            "akeneo_reference_entity" or
            "akeneo_reference_entity_collection" => NopTargetType.SpecificationAttribute,
            _ => NopTargetType.Ignore
        };
    }

    public IReadOnlyList<NopTargetType> GetAllowedTargetTypes(AkeneoAttributeDefinition attribute)
    {
        return Normalize(attribute.Type) switch
        {
            "pim_catalog_identifier" =>
            [
                NopTargetType.Ignore,
                NopTargetType.ProductField
            ],

            "pim_catalog_text" =>
            [
                NopTargetType.Ignore,
                NopTargetType.ProductField,
                NopTargetType.SpecificationAttribute,
                NopTargetType.ProductAttribute,
                NopTargetType.SeoField,
                NopTargetType.CustomProperty
            ],

            "pim_catalog_textarea" =>
            [
                NopTargetType.Ignore,
                NopTargetType.ProductField,
                NopTargetType.SpecificationAttribute,
                NopTargetType.SeoField,
                NopTargetType.CustomProperty
            ],

            "pim_catalog_price_collection" =>
            [
                NopTargetType.Ignore,
                NopTargetType.ProductField
            ],

            "pim_catalog_simpleselect" =>
            [
                NopTargetType.Ignore,
                NopTargetType.ProductAttribute,
                NopTargetType.SpecificationAttribute,
                NopTargetType.Manufacturer,
                NopTargetType.Category,
                NopTargetType.CustomProperty
            ],

            "pim_catalog_multiselect" =>
            [
                NopTargetType.Ignore,
                NopTargetType.ProductAttribute,
                NopTargetType.SpecificationAttribute,
                NopTargetType.Category,
                NopTargetType.CustomProperty
            ],

            "pim_catalog_boolean" =>
            [
                NopTargetType.Ignore,
                NopTargetType.ProductField,
                NopTargetType.SpecificationAttribute,
                NopTargetType.CustomProperty
            ],

            "pim_catalog_number" or
            "pim_catalog_metric" or
            "pim_catalog_date" =>
            [
                NopTargetType.Ignore,
                NopTargetType.ProductField,
                NopTargetType.SpecificationAttribute,
                NopTargetType.CustomProperty
            ],

            "akeneo_reference_entity" or
            "akeneo_reference_entity_collection" =>
            [
                NopTargetType.Ignore,
                NopTargetType.ProductField,
                NopTargetType.SpecificationAttribute,
                NopTargetType.SeoField,
                NopTargetType.Manufacturer,
                NopTargetType.CustomProperty
            ],

            _ =>
            [
                NopTargetType.Ignore,
                NopTargetType.CustomProperty
            ]
        };
    }

    public string ResolveDefaultTargetKey(
        AkeneoAttributeDefinition attribute,
        NopTargetType targetType)
    {
        if (targetType == NopTargetType.Ignore)
            return string.Empty;

        var code = Normalize(attribute.Code);
        var type = Normalize(attribute.Type);

        if (targetType == NopTargetType.ProductField)
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
                "stock" or "stock_quantity" or "quantity" => "StockQuantity",
                "gtin" or "barcode" => "Gtin",
                "mpn" or "manufacturer_part_number" => "ManufacturerPartNumber",
                "enabled" or "published" => "Published",
                _ => string.Empty
            };
        }

        if (targetType == NopTargetType.SeoField)
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

        if (targetType == NopTargetType.CustomProperty)
            return attribute.Code;

        return string.Empty;
    }

    private static string Normalize(string type) =>
        type?.Trim().ToLowerInvariant() ?? string.Empty;
}