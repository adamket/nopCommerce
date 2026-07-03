using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Extensions;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;
using static Apt.Nop.Plugin.Misc.AkeneoConnection.Models.AkeneoProductMappingPreviewModel;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;

public class AkeneoProductMappingFactory : IAkeneoProductMappingFactory
{
    private const string DefaultLocale = "en_US";
    private const string DefaultChannel = "ecommerce";
    private const string DefaultCurrency = "USD";

    private readonly IAkeneoApiClient _akeneoApiClient;
    private readonly IAkeneoAttributeMappingService _akeneoAttributeMappingService;
    private readonly IAkeneoNopEntityMappingService _akeneoNopEntityMappingService;
    private readonly IAkeneoProductValueResolver _akeneoProductValueResolver;
    private readonly IProductService _productService;
    private readonly ISpecificationAttributeService _specificationAttributeService;
    private readonly IProductAttributeService _productAttributeService;

    public AkeneoProductMappingFactory(
        IAkeneoApiClient akeneoApiClient,
        IAkeneoAttributeMappingService akeneoAttributeMappingService,
        IAkeneoNopEntityMappingService akeneoNopEntityMappingService,
        IAkeneoProductValueResolver akeneoProductValueResolver,
        IProductService productService,
        ISpecificationAttributeService specificationAttributeService,
        IProductAttributeService productAttributeService)
    {
        _akeneoApiClient = akeneoApiClient;
        _akeneoAttributeMappingService = akeneoAttributeMappingService;
        _akeneoNopEntityMappingService = akeneoNopEntityMappingService;
        _akeneoProductValueResolver = akeneoProductValueResolver;
        _productService = productService;
        _specificationAttributeService = specificationAttributeService;
        _productAttributeService = productAttributeService;
    }

    public async Task<AkeneoProductMappingPreviewModel> PreviewProductMappingAsync(
     string akeneoIdentifier,
     string locale = null,
     string channel = null,
     string currency = null,
     CancellationToken cancellationToken = default)
    {
        locale = Normalize(locale, DefaultLocale);
        channel = Normalize(channel, DefaultChannel);
        currency = Normalize(currency, DefaultCurrency);

        var model = new AkeneoProductMappingPreviewModel
        {
            AkeneoIdentifier = akeneoIdentifier,
            Locale = locale,
            Channel = channel,
            Currency = currency,
            HasSearched = true
        };

        if (string.IsNullOrWhiteSpace(akeneoIdentifier))
        {
            model.Errors.Add("Enter an Akeneo identifier/SKU.");
            return model;
        }

        akeneoIdentifier = akeneoIdentifier.Trim();

        var akeneoProduct = await FindAkeneoProductByIdentifierAsync(
            akeneoIdentifier,
            cancellationToken);

        if (!akeneoProduct.HasValue)
        {
            model.AkeneoProductFound = false;
            model.Errors.Add($"No Akeneo product was found for identifier \"{akeneoIdentifier}\".");
            return model;
        }

        model.AkeneoProductFound = true;
        model.AkeneoProductUuid = akeneoProduct.Value.GetRootString("uuid");

        if (string.IsNullOrWhiteSpace(model.AkeneoProductUuid))
        {
            model.Errors.Add("The Akeneo product was found, but it did not contain a UUID. Import cannot run.");
            return model;
        }

        var savedMappings = await _akeneoAttributeMappingService
            .GetAllAkeneoAttributeMappingsAsync();

        var activeMappings = savedMappings
            .Where(mapping => mapping.NopTargetTypeId != (int)NopTargetType.Ignore)
            .OrderBy(mapping => mapping.AkeneoAttributeCode)
            .ToList();

        if (!activeMappings.Any())
        {
            model.Warnings.Add("No active attribute mappings are configured.");
            return model;
        }

        var entityMappings = await _akeneoNopEntityMappingService
            .GetAkeneoNopEntityMappingsAsync(akeneoEntityType: AkeneoEntityType.Attribute);

        var specificationAttributes = await _specificationAttributeService
            .GetAllSpecificationAttributesAsync();

        var productAttributes = await _productAttributeService
            .GetAllProductAttributesAsync();

        foreach (var mapping in activeMappings)
        {
            var value = _akeneoProductValueResolver.GetValue(
                akeneoProduct.Value,
                mapping.AkeneoAttributeCode,
                locale: !string.IsNullOrWhiteSpace(mapping.Locale) ? mapping.Locale : locale,
                channel: !string.IsNullOrWhiteSpace(mapping.Channel) ? mapping.Channel : channel,
                currency: currency);

            if (mapping.IsRequired && string.IsNullOrWhiteSpace(value))
            {
                model.Errors.Add(
                    $"Required Akeneo attribute \"{mapping.AkeneoAttributeCode}\" did not produce a value.");
            }

            var targetType = (NopTargetType)mapping.NopTargetTypeId;

            switch (targetType)
            {
                case NopTargetType.ProductField:
                    AddProductFieldPreview(model, mapping, value);
                    break;

                case NopTargetType.SpecificationAttribute:
                    AddSpecificationAttributePreview(
                        model,
                        mapping,
                        value,
                        specificationAttributes);
                    break;

                case NopTargetType.ProductAttribute:
                    AddProductAttributePreview(
                        model,
                        mapping,
                        value,
                        productAttributes);
                    break;

                case NopTargetType.SeoField:
                    AddSeoFieldPreview(model, mapping, value);
                    break;

                case NopTargetType.CustomProperty:
                    AddCustomPropertyPreview(model, mapping, value);
                    break;

                case NopTargetType.Manufacturer:
                    model.Warnings.Add(
                        $"Manufacturer mapping preview is not implemented yet for Akeneo attribute \"{mapping.AkeneoAttributeCode}\".");
                    break;

                case NopTargetType.Category:
                    model.Warnings.Add(
                        $"Category mapping preview is not implemented yet for Akeneo attribute \"{mapping.AkeneoAttributeCode}\".");
                    break;
            }
        }

        var mappedSku = model.ProductFields.FirstOrDefault(field =>
            string.Equals(field.TargetKey, "Sku", StringComparison.OrdinalIgnoreCase));

        var skuToFind = !string.IsNullOrWhiteSpace(mappedSku?.Value)
            ? mappedSku.Value
            : akeneoIdentifier;

        var nopProduct = await _productService.GetProductBySkuAsync(skuToFind);

        if (nopProduct != null)
        {
            model.NopProductFound = true;
            model.NopProductId = nopProduct.Id;
            model.Action = "Would update existing nopCommerce product.";
        }
        else
        {
            model.NopProductFound = false;
            model.Action = "Would create new nopCommerce product.";
        }

        AddFinalWarnings(model);

        return model;
    }

    private async Task<JsonElement?> FindAkeneoProductByIdentifierAsync(
        string akeneoIdentifier,
        CancellationToken cancellationToken)
    {
        var searchJson = JsonSerializer.Serialize(new
        {
            identifier = new[]
            {
                new
                {
                    @operator = "=",
                    value = akeneoIdentifier
                }
            }
        });

        var products = await _akeneoApiClient.GetProductsAsync(
            searchJson,
            limit: 1,
            cancellationToken: cancellationToken);

        return products.FirstOrDefault();
    }

    private static void AddProductFieldPreview(
        AkeneoProductMappingPreviewModel model,
        AkeneoAttributeMapping mapping,
        string value)
    {
        if (string.IsNullOrWhiteSpace(mapping.NopTargetKey))
        {
            model.Warnings.Add(
                $"Akeneo attribute \"{mapping.AkeneoAttributeCode}\" is mapped to Product Field but no Target Key is configured.");
            return;
        }

        model.ProductFields.Add(new AkeneoMappedFieldPreviewModel
        {
            AkeneoAttributeCode = mapping.AkeneoAttributeCode,
            TargetKey = mapping.NopTargetKey,
            Value = value,
            IsRequired = mapping.IsRequired
        });
    }

    private static void AddSeoFieldPreview(
        AkeneoProductMappingPreviewModel model,
        AkeneoAttributeMapping mapping,
        string value)
    {
        if (string.IsNullOrWhiteSpace(mapping.NopTargetKey))
        {
            model.Warnings.Add(
                $"Akeneo attribute \"{mapping.AkeneoAttributeCode}\" is mapped to SEO Field but no Target Key is configured.");
            return;
        }

        model.SeoFields.Add(new AkeneoMappedFieldPreviewModel
        {
            AkeneoAttributeCode = mapping.AkeneoAttributeCode,
            TargetKey = mapping.NopTargetKey,
            Value = value,
            IsRequired = mapping.IsRequired
        });
    }

    private static void AddCustomPropertyPreview(
        AkeneoProductMappingPreviewModel model,
        AkeneoAttributeMapping mapping,
        string value)
    {
        model.CustomProperties.Add(new AkeneoMappedFieldPreviewModel
        {
            AkeneoAttributeCode = mapping.AkeneoAttributeCode,
            TargetKey = string.IsNullOrWhiteSpace(mapping.NopTargetKey)
                ? mapping.AkeneoAttributeCode
                : mapping.NopTargetKey,
            Value = value,
            IsRequired = mapping.IsRequired
        });
    }

    private static void AddSpecificationAttributePreview(
        AkeneoProductMappingPreviewModel model,
        AkeneoAttributeMapping mapping,
        string value,
        IList<SpecificationAttribute> specificationAttributes)
    {
        var id = mapping.NopTargetEntityId ?? 0;
        if (id <= 0)
        {
            model.Warnings.Add($"Akeneo attribute \"{mapping.AkeneoAttributeCode}\" is mapped to Specification Attribute, but no nopCommerce specification attribute is selected.");
            return;
        }

        var specificationAttribute = specificationAttributes.FirstOrDefault(a => a.Id == id);

        model.SpecificationAttributes.Add(new AkeneoMappedAttributePreviewModel
        {
            AkeneoAttributeCode = mapping.AkeneoAttributeCode,
            NopAttributeId = id,
            NopAttributeName = specificationAttribute?.Name ?? $"SpecificationAttributeId {id}",
            Value = value,
            IsRequired = mapping.IsRequired
        });
    }

    private static void AddProductAttributePreview(
        AkeneoProductMappingPreviewModel model,
        AkeneoAttributeMapping mapping,
        string value,
        IList<ProductAttribute> productAttributes)
    {
        var id = mapping.NopTargetEntityId ?? 0;

        if (id <= 0)
        {
            model.Warnings.Add(
                $"Akeneo attribute \"{mapping.AkeneoAttributeCode}\" is mapped to Product Attribute, but no nopCommerce product attribute is selected.");

            return;
        }

        var productAttribute = productAttributes.FirstOrDefault(attribute =>
            attribute.Id == id);

        model.ProductAttributes.Add(new AkeneoMappedAttributePreviewModel
        {
            AkeneoAttributeCode = mapping.AkeneoAttributeCode,
            NopAttributeId = id,
            NopAttributeName = productAttribute?.Name ?? $"ProductAttributeId {id}",
            Value = value,
            IsRequired = mapping.IsRequired
        });
    }

    private static void AddFinalWarnings(
        AkeneoProductMappingPreviewModel model)
    {
        if (!model.ProductFields.Any(field =>
                string.Equals(field.TargetKey, "Sku", StringComparison.OrdinalIgnoreCase)))
        {
            model.Warnings.Add("No active mapping targets Product.Sku.");
        }

        if (!model.ProductFields.Any(field =>
                string.Equals(field.TargetKey, "Name", StringComparison.OrdinalIgnoreCase)))
        {
            model.Warnings.Add("No active mapping targets Product.Name.");
        }

        if (!model.ProductFields.Any(field =>
                string.Equals(field.TargetKey, "Price", StringComparison.OrdinalIgnoreCase)))
        {
            model.Warnings.Add("No active mapping targets Product.Price.");
        }
    }

    //private static string GetRootString(JsonElement element, string propertyName)
    //{
    //    if (!element.TryGetProperty(propertyName, out var property))
    //        return null;

    //    return property.ValueKind == JsonValueKind.String
    //        ? property.GetString()
    //        : null;
    //}

    private static string Normalize(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim();
    }
}