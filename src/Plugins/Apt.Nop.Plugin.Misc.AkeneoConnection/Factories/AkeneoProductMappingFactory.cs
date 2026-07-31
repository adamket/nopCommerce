using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
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
    private readonly IAkeneoProductValueResolver _akeneoProductValueResolver;
    private readonly IAkeneoReferenceEntityValueResolver _referenceEntityValueResolver;
    private readonly IAkeneoValueTransformationService _valueTransformationService;
    private readonly IAkeneoValueTemplateRenderer _valueTemplateRenderer;
    private readonly IAkeneoFamilyMappingService _familyMappingService;
    private readonly IAkeneoProductModelHierarchyResolver _productModelHierarchyResolver;
    private readonly IAkeneoNopEntityMappingService _entityMappingService;
    private readonly IProductService _productService;
    private readonly ISpecificationAttributeService _specificationAttributeService;
    private readonly IProductAttributeService _productAttributeService;

    public AkeneoProductMappingFactory(
        IAkeneoApiClient akeneoApiClient,
        IAkeneoAttributeMappingService akeneoAttributeMappingService,
        IAkeneoProductValueResolver akeneoProductValueResolver,
        IAkeneoReferenceEntityValueResolver referenceEntityValueResolver,
        IAkeneoValueTransformationService valueTransformationService,
        IAkeneoValueTemplateRenderer valueTemplateRenderer,
        IAkeneoFamilyMappingService familyMappingService,
        IAkeneoProductModelHierarchyResolver productModelHierarchyResolver,
        IAkeneoNopEntityMappingService entityMappingService,
        IProductService productService,
        ISpecificationAttributeService specificationAttributeService,
        IProductAttributeService productAttributeService)
    {
        _akeneoApiClient = akeneoApiClient;
        _akeneoAttributeMappingService = akeneoAttributeMappingService;
        _akeneoProductValueResolver = akeneoProductValueResolver;
        _referenceEntityValueResolver = referenceEntityValueResolver;
        _valueTransformationService = valueTransformationService;
        _valueTemplateRenderer = valueTemplateRenderer;
        _familyMappingService = familyMappingService;
        _productModelHierarchyResolver = productModelHierarchyResolver;
        _entityMappingService = entityMappingService;
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
        locale = locale.TrimOr(DefaultLocale);
        channel = channel.TrimOr(DefaultChannel);
        currency = currency.TrimOr(DefaultCurrency);

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
            model.Errors.Add("Enter an Akeneo identifier/SKU or product model code.");
            return model;
        }

        akeneoIdentifier = akeneoIdentifier.Trim();

        var akeneoSource = await FindAkeneoProductByIdentifierAsync(
            akeneoIdentifier,
            cancellationToken);

        var sourceEntityType = AkeneoEntityType.Product;

        if (akeneoSource == null)
        {
            akeneoSource = await _akeneoApiClient.GetProductModelByCodeAsync(
                akeneoIdentifier,
                cancellationToken);

            sourceEntityType = AkeneoEntityType.ProductModel;
        }

        if (akeneoSource == null)
        {
            model.AkeneoProductFound = false;
            model.Errors.Add(
                $"No Akeneo product or product model was found for \"{akeneoIdentifier}\".");
            return model;
        }

        model.AkeneoProductFound = true;
        model.AkeneoEntityTypeId = (int)sourceEntityType;
        model.AkeneoProductUuid = sourceEntityType == AkeneoEntityType.Product
            ? akeneoSource.Uuid
            : null;
        model.AkeneoProductModelCode = sourceEntityType == AkeneoEntityType.ProductModel
            ? akeneoSource.Code
            : null;

        if (sourceEntityType == AkeneoEntityType.Product &&
            string.IsNullOrWhiteSpace(model.AkeneoProductUuid))
        {
            model.Errors.Add("The Akeneo product was found, but it did not contain a UUID. Import cannot run.");
            return model;
        }

        if (sourceEntityType == AkeneoEntityType.ProductModel &&
            string.IsNullOrWhiteSpace(model.AkeneoProductModelCode))
        {
            model.Errors.Add("The Akeneo product model was found, but it did not contain a code. Import cannot run.");
            return model;
        }

        var previewSource = sourceEntityType == AkeneoEntityType.ProductModel
            ? await BuildEffectiveProductModelPreviewSourceAsync(
                akeneoSource,
                model,
                cancellationToken)
            : await BuildEffectivePreviewSourceAsync(
                akeneoSource,
                model,
                cancellationToken);

        if (sourceEntityType == AkeneoEntityType.ProductModel)
            model.AkeneoProductModelCode = previewSource.Code;

        var savedMappings = await _akeneoAttributeMappingService
            .GetEffectiveMappingsAsync(previewSource.Family ?? akeneoSource.Family);

        var mappingEntityScope =
            AkeneoAttributeMappingScopeHelper.ResolveDefaultCurrentScope(
                previewSource,
                sourceEntityType);

        var activeMappings = savedMappings
            .Where(mapping =>
                AkeneoAttributeMappingScopeHelper.AppliesTo(
                    mapping,
                    mappingEntityScope))
            .Where(mapping => mapping.NopTargetTypeId != (int)NopTargetType.Ignore)
            .OrderBy(mapping => mapping.ValueModeId)
            .ThenBy(mapping => mapping.Name)
            .ThenBy(mapping => mapping.AkeneoAttributeCode)
            .ThenBy(mapping => mapping.AkeneoReferenceEntityAttributeCode)
            .ToList();

        if (!activeMappings.Any())
        {
            model.Warnings.Add("No active attribute mappings are configured.");
            return model;
        }

        var fallbackSourcesByMappingId =
            (await _akeneoAttributeMappingService.GetAllFallbackSourcesAsync())
            .Where(source => activeMappings.Any(mapping =>
                mapping.Id == source.AttributeMappingId))
            .GroupBy(source => source.AttributeMappingId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<AkeneoAttributeMappingFallbackSource>)group
                    .OrderBy(source => source.DisplayOrder)
                    .ThenBy(source => source.Id)
                    .ToList());

        var specificationAttributes = await _specificationAttributeService
            .GetAllSpecificationAttributesAsync();

        var productAttributes = await _productAttributeService
            .GetAllProductAttributesAsync();

        var previewValues = new List<(AkeneoAttributeMapping Mapping, string Value)>();

        // Resolve ordinary mappings first so {sku} uses the same mapped SKU
        // that the real synchronization pipeline uses.
        foreach (var mapping in activeMappings.Where(mapping =>
                     mapping.ValueModeId !=
                     (int)AkeneoAttributeMappingValueMode.Template))
        {
            var value = await ResolveSingleAttributePreviewValueAsync(
                previewSource,
                mapping,
                fallbackSourcesByMappingId,
                locale,
                channel,
                currency,
                model,
                cancellationToken);

            previewValues.Add((mapping, value));
        }

        var effectiveSku = previewValues
            .FirstOrDefault(item =>
                item.Mapping.NopTargetTypeId == (int)NopTargetType.ProductField &&
                string.Equals(
                    item.Mapping.NopTargetKey,
                    "Sku",
                    StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(item.Value))
            .Value;

        effectiveSku ??= previewSource.Identifier ?? previewSource.Code;

        foreach (var mapping in activeMappings.Where(mapping =>
                     mapping.ValueModeId ==
                     (int)AkeneoAttributeMappingValueMode.Template))
        {
            var value = ResolveTemplatePreviewValue(
                previewSource,
                mapping,
                effectiveSku,
                previewSource.Family ?? akeneoSource.Family,
                locale,
                channel,
                currency,
                model);

            previewValues.Add((mapping, value));
        }

        foreach (var (mapping, value) in previewValues)
        {
            AddPreviewValue(
                model,
                mapping,
                value,
                specificationAttributes,
                productAttributes);
        }

        var mappedSku = model.ProductFields.FirstOrDefault(field =>
            string.Equals(field.TargetKey, "Sku", StringComparison.OrdinalIgnoreCase));

        var skuToFind = !string.IsNullOrWhiteSpace(mappedSku?.Value)
            ? mappedSku.Value
            : previewSource.Identifier ?? previewSource.Code ?? akeneoIdentifier;

        var nopProduct = await FindMappedNopProductAsync(
            previewSource,
            sourceEntityType);

        nopProduct ??= await _productService.GetProductBySkuAsync(skuToFind);

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

    private async Task<AkeneoProductDefinition> BuildEffectivePreviewSourceAsync(
        AkeneoProductDefinition product,
        AkeneoProductMappingPreviewModel model,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(product.Parent))
            return product;

        var ancestors = new List<AkeneoProductDefinition>();
        var visitedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var currentCode = product.Parent.Trim();
        var cycleDetected = false;

        while (!string.IsNullOrWhiteSpace(currentCode))
        {
            if (!visitedCodes.Add(currentCode))
            {
                cycleDetected = true;
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var productModel = await _akeneoApiClient
                .GetProductModelByCodeAsync(currentCode, cancellationToken);

            if (productModel == null)
            {
                model.Warnings.Add(
                    $"Product-model inheritance could not be previewed because Akeneo product model '{currentCode}' was not found.");
                break;
            }

            ancestors.Add(productModel);
            currentCode = productModel.Parent?.Trim();
        }

        if (ancestors.Count == 0)
            return product;

        if (cycleDetected)
        {
            model.Warnings.Add(
                $"A product-model inheritance cycle was detected at '{currentCode}'. Preview used the values resolved before the cycle.");
        }

        var familyMapping = await _familyMappingService
            .GetByFamilyCodeAsync(product.Family);

        var hierarchyMode = familyMapping is { Enabled: true }
            ? familyMapping.ProductModelHierarchyMode
            : AkeneoProductModelHierarchyMode.ImmediateParentProductModel;

        var hierarchy = _productModelHierarchyResolver.Resolve(
            product,
            ancestors,
            hierarchyMode);

        return hierarchy.EffectiveLeaf;
    }

    private async Task<AkeneoProductDefinition>
        BuildEffectiveProductModelPreviewSourceAsync(
            AkeneoProductDefinition productModel,
            AkeneoProductMappingPreviewModel model,
            CancellationToken cancellationToken)
    {
        var selectedCode = productModel.Code?.Trim();
        if (string.IsNullOrWhiteSpace(selectedCode))
            return productModel;

        var ancestors = new List<AkeneoProductDefinition>();
        var visitedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            selectedCode
        };
        var currentCode = productModel.Parent?.Trim();
        var cycleDetected = false;

        while (!string.IsNullOrWhiteSpace(currentCode))
        {
            if (!visitedCodes.Add(currentCode))
            {
                cycleDetected = true;
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var ancestor = await _akeneoApiClient
                .GetProductModelByCodeAsync(currentCode, cancellationToken);

            if (ancestor == null)
            {
                model.Warnings.Add(
                    $"Product-model inheritance could not be fully previewed because Akeneo product model '{currentCode}' was not found.");
                break;
            }

            ancestors.Add(ancestor);
            currentCode = ancestor.Parent?.Trim();
        }

        if (cycleDetected)
        {
            model.Warnings.Add(
                $"A product-model inheritance cycle was detected at '{currentCode}'. Preview used the values resolved before the cycle.");
        }

        var familyMapping = await _familyMappingService
            .GetByFamilyCodeAsync(productModel.Family);

        var hierarchyMode = familyMapping is { Enabled: true }
            ? familyMapping.ProductModelHierarchyMode
            : AkeneoProductModelHierarchyMode.ImmediateParentProductModel;

        // Treat the selected model as the parent of a synthetic leaf so the
        // same hierarchy-selection and inheritance rules used by normal leaf
        // synchronization determine the effective nopCommerce parent model.
        var syntheticLeaf = new AkeneoProductDefinition
        {
            Parent = selectedCode,
            Family = productModel.Family,
            FamilyVariant = productModel.FamilyVariant,
            Values = JsonSerializer.SerializeToElement(new Dictionary<string, object>())
        };

        var modelChain = new List<AkeneoProductDefinition> { productModel };
        modelChain.AddRange(ancestors);

        var hierarchy = _productModelHierarchyResolver.Resolve(
            syntheticLeaf,
            modelChain,
            hierarchyMode);

        var effectiveModel = hierarchy.EffectiveParentProductModel;

        if (!string.Equals(
                selectedCode,
                effectiveModel.Code,
                StringComparison.OrdinalIgnoreCase))
        {
            model.Warnings.Add(
                $"The family hierarchy configuration resolves product model '{selectedCode}' to nopCommerce parent product model '{effectiveModel.Code}'. The preview and one-time import target the resolved parent model.");
        }

        return effectiveModel;
    }

    private async Task<Product> FindMappedNopProductAsync(
        AkeneoProductDefinition source,
        AkeneoEntityType sourceEntityType)
    {
        int? mappedProductId;

        if (sourceEntityType == AkeneoEntityType.Product)
        {
            mappedProductId = await _entityMappingService
                .GetMappedNopEntityIdByAkeneoUuidAsync(
                    sourceEntityType,
                    source.Uuid,
                    NopEntityType.Product);
        }
        else
        {
            mappedProductId = await _entityMappingService
                .GetMappedNopEntityIdByAkeneoCodeAsync(
                    sourceEntityType,
                    source.Code,
                    NopEntityType.Product);
        }

        return mappedProductId.HasValue && mappedProductId.Value > 0
            ? await _productService.GetProductByIdAsync(mappedProductId.Value)
            : null;
    }

    private async Task<string> ResolveSingleAttributePreviewValueAsync(
        AkeneoProductDefinition source,
        AkeneoAttributeMapping mapping,
        IReadOnlyDictionary<int, IReadOnlyList<AkeneoAttributeMappingFallbackSource>> fallbackSourcesByMappingId,
        string locale,
        string channel,
        string currency,
        AkeneoProductMappingPreviewModel model,
        CancellationToken cancellationToken)
    {
        var mappingLocale = !string.IsNullOrWhiteSpace(mapping.Locale)
            ? mapping.Locale
            : locale;
        var mappingChannel = !string.IsNullOrWhiteSpace(mapping.Channel)
            ? mapping.Channel
            : channel;

        AkeneoResolvedProductValue resolvedValue = null;

        foreach (var sourceMapping in BuildOrderedSourceMappings(
                     mapping,
                     fallbackSourcesByMappingId))
        {
            cancellationToken.ThrowIfCancellationRequested();

            resolvedValue = await ResolveSourceValueAsync(
                source,
                sourceMapping,
                mappingLocale,
                mappingChannel,
                currency,
                cancellationToken);

            if (HasDisplayValue(resolvedValue))
                break;
        }

        return ApplyPreviewTransform(
            mapping,
            resolvedValue,
            model);
    }

    private string ResolveTemplatePreviewValue(
        AkeneoProductDefinition source,
        AkeneoAttributeMapping mapping,
        string effectiveSku,
        string familyCode,
        string locale,
        string channel,
        string currency,
        AkeneoProductMappingPreviewModel model)
    {
        var mappingLocale = !string.IsNullOrWhiteSpace(mapping.Locale)
            ? mapping.Locale
            : locale;
        var mappingChannel = !string.IsNullOrWhiteSpace(mapping.Channel)
            ? mapping.Channel
            : channel;

        var rendered = _valueTemplateRenderer.Render(
            mapping.ValueTemplate,
            new AkeneoValueTemplateContext
            {
                Source = source,
                Locale = mappingLocale,
                Channel = mappingChannel,
                Currency = currency,
                FamilyCode = familyCode,
                Sku = effectiveSku
            });

        var displayName = AkeneoMappingHelper.GetSourceDisplayName(mapping);

        if (rendered.Errors.Count > 0)
        {
            model.Errors.Add(
                $"Computed mapping \"{displayName}\" could not be rendered: {string.Join(" ", rendered.Errors)}");
            return string.Empty;
        }

        if (rendered.MissingTokens.Count > 0)
        {
            var message =
                $"Computed mapping \"{displayName}\" could not be rendered because these tokens had no value: {string.Join(", ", rendered.MissingTokens.Select(token => $"{{{token}}}"))}.";

            if (mapping.IsRequired)
                model.Errors.Add(message);
            else
                model.Warnings.Add(message);

            return string.Empty;
        }

        var resolvedValue = new AkeneoResolvedProductValue
        {
            AttributeCode = mapping.MappingKey,
            Locale = mappingLocale,
            Channel = mappingChannel,
            Currency = currency,
            SourceAttributeType = "computed_template",
            DisplayValue = rendered.Value,
            DisplayValues = string.IsNullOrWhiteSpace(rendered.Value)
                ? Array.Empty<string>()
                : new[] { rendered.Value }
        };

        return ApplyPreviewTransform(
            mapping,
            resolvedValue,
            model);
    }

    private async Task<AkeneoResolvedProductValue> ResolveSourceValueAsync(
        AkeneoProductDefinition source,
        AkeneoAttributeMapping sourceMapping,
        string locale,
        string channel,
        string currency,
        CancellationToken cancellationToken)
    {
        if (AkeneoMappingHelper.IsReferenceEntityMapping(sourceMapping) &&
            !string.IsNullOrWhiteSpace(
                sourceMapping.AkeneoReferenceEntityAttributeCode))
        {
            return await _referenceEntityValueResolver.ResolveAsync(
                source,
                sourceMapping,
                locale,
                channel,
                currency,
                cancellationToken);
        }

        return _akeneoProductValueResolver.TryGetValue(
            source,
            sourceMapping.AkeneoAttributeCode,
            out var value,
            locale,
            channel,
            currency)
                ? value
                : null;
    }

    private string ApplyPreviewTransform(
        AkeneoAttributeMapping mapping,
        AkeneoResolvedProductValue value,
        AkeneoProductMappingPreviewModel model)
    {
        var displayName = AkeneoMappingHelper.GetSourceDisplayName(mapping);

        if (!HasDisplayValue(value))
        {
            if (mapping.IsRequired)
            {
                model.Errors.Add(
                    $"Required Akeneo source \"{displayName}\" did not produce a value.");
            }

            return string.Empty;
        }

        var transformed = _valueTransformationService.Transform(
            value,
            mapping);

        if (!transformed.Success)
        {
            model.Errors.Add(
                $"Transform failed for Akeneo source \"{displayName}\": {transformed.Error}");
            return string.Empty;
        }

        return transformed.Value?.DisplayValue ?? string.Empty;
    }

    private static IEnumerable<AkeneoAttributeMapping>
        BuildOrderedSourceMappings(
            AkeneoAttributeMapping primaryMapping,
            IReadOnlyDictionary<int, IReadOnlyList<AkeneoAttributeMappingFallbackSource>> fallbackSourcesByMappingId)
    {
        yield return primaryMapping;

        if (!fallbackSourcesByMappingId.TryGetValue(
                primaryMapping.Id,
                out var fallbackSources))
        {
            yield break;
        }

        foreach (var fallback in fallbackSources
                     .OrderBy(source => source.DisplayOrder)
                     .ThenBy(source => source.Id))
        {
            yield return new AkeneoAttributeMapping
            {
                AkeneoAttributeCode = fallback.AkeneoAttributeCode,
                AkeneoAttributeTypeId = fallback.AkeneoAttributeTypeId,
                AkeneoReferenceEntityCode =
                    fallback.AkeneoReferenceEntityCode,
                AkeneoReferenceEntityAttributeCode =
                    fallback.AkeneoReferenceEntityAttributeCode
            };
        }
    }

    private static bool HasDisplayValue(
        AkeneoResolvedProductValue value)
    {
        return !string.IsNullOrWhiteSpace(value?.DisplayValue) ||
               value?.DisplayValues is { Count: > 0 };
    }

    private static void AddPreviewValue(
        AkeneoProductMappingPreviewModel model,
        AkeneoAttributeMapping mapping,
        string value,
        IList<SpecificationAttribute> specificationAttributes,
        IList<ProductAttribute> productAttributes)
    {
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
                    $"Manufacturer mapping preview is not implemented yet for Akeneo source \"{AkeneoMappingHelper.GetSourceDisplayName(mapping)}\".");
                break;

            case NopTargetType.Category:
                model.Warnings.Add(
                    $"Category mapping preview is not implemented yet for Akeneo source \"{AkeneoMappingHelper.GetSourceDisplayName(mapping)}\".");
                break;
        }
    }



    private async Task<AkeneoProductDefinition> FindAkeneoProductByIdentifierAsync(
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
                $"Akeneo attribute \"{AkeneoMappingHelper.GetSourceDisplayName(mapping)}\" is mapped to Product Field but no Target Key is configured.");
            return;
        }

        model.ProductFields.Add(new AkeneoMappedFieldPreviewModel
        {
            AkeneoAttributeCode = AkeneoMappingHelper.GetSourceDisplayName(mapping),
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
                $"Akeneo attribute \"{AkeneoMappingHelper.GetSourceDisplayName(mapping)}\" is mapped to SEO Field but no Target Key is configured.");
            return;
        }

        model.SeoFields.Add(new AkeneoMappedFieldPreviewModel
        {
            AkeneoAttributeCode = AkeneoMappingHelper.GetSourceDisplayName(mapping),
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
            AkeneoAttributeCode = AkeneoMappingHelper.GetSourceDisplayName(mapping),
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
            model.Warnings.Add($"Akeneo attribute \"{AkeneoMappingHelper.GetSourceDisplayName(mapping)}\" is mapped to Specification Attribute, but no nopCommerce specification attribute is selected.");
            return;
        }

        var specificationAttribute = specificationAttributes.FirstOrDefault(a => a.Id == id);

        model.SpecificationAttributes.Add(new AkeneoMappedAttributePreviewModel
        {
            AkeneoAttributeCode = AkeneoMappingHelper.GetSourceDisplayName(mapping),
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
                $"Akeneo attribute \"{AkeneoMappingHelper.GetSourceDisplayName(mapping)}\" is mapped to Product Attribute, but no nopCommerce product attribute is selected.");

            return;
        }

        var productAttribute = productAttributes.FirstOrDefault(attribute =>
            attribute.Id == id);

        model.ProductAttributes.Add(new AkeneoMappedAttributePreviewModel
        {
            AkeneoAttributeCode = AkeneoMappingHelper.GetSourceDisplayName(mapping),
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

   
}