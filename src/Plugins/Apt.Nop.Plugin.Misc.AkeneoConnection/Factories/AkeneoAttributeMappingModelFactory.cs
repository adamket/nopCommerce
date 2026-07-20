using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;

public class AkeneoAttributeMappingModelFactory(
    IAkeneoApiClient akeneoApiClient,
    IAkeneoAttributeMappingService akeneoAttributeMappingService,
    IAkeneoNopEntityMappingService akeneoNopEntityMappingService,
    ISpecificationAttributeService specificationAttributeService,
    IProductAttributeService productAttributeService,
    IAkeneoTargetTypeResolver targetTypeResolver)
    : IAkeneoAttributeMappingModelFactory
{
    public async Task<AkeneoAttributeMappingListModel> PrepareAttributeMappingListModelAsync(string akeneoFamilyCode = null)
    {
        return await PrepareAttributeMappingListModelAsync(new AkeneoAttributeMappingListModel{AkeneoFamilyCode = akeneoFamilyCode});
    }

    public async Task<AkeneoAttributeMappingListModel> PrepareAttributeMappingListModelAsync(
        AkeneoAttributeMappingListModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        IReadOnlyList<AkeneoAttributeDefinition> akeneoAttributes;
        IReadOnlyList<AkeneoFamilyDefinition> akeneoFamilies;

        try
        {
            akeneoAttributes = await akeneoApiClient.GetAttributesAsync();
            if (!string.IsNullOrWhiteSpace(model.AkeneoFamilyCode))
            {
                var selectedFamily = await akeneoApiClient
                    .GetFamilyByCodeAsync(model.AkeneoFamilyCode);

                if (selectedFamily == null)
                {
                    model.Warnings.Add(
                        $"Akeneo family '{model.AkeneoFamilyCode}' was not found.");

                    model.Mappings.Clear();
                    return model;
                }

                var familyAttributeCodes = selectedFamily.Attributes
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                akeneoAttributes = akeneoAttributes
                    .Where(attribute =>
                        familyAttributeCodes.Contains(attribute.Code))
                    .ToList();
            }

            akeneoFamilies = await akeneoApiClient.GetFamiliesAsync();

            model.AvailableAkeneoFamilies = akeneoFamilies
                .OrderBy(family => family.GetLabel())
                .Select(family => new SelectListItem
                {
                    Text = family.GetLabel(),
                    Value = family.Code,
                    Selected = string.Equals(
                        family.Code,
                        model.AkeneoFamilyCode,
                        StringComparison.OrdinalIgnoreCase)
                })
                .ToList();
        }
        catch (Exception)
        {
            model.Warnings.Add("Could not load akeneo attributes.  Verify your Akeneo connection and try again.");
            return model;
        }

        var existingAttributeMappings =
            await akeneoAttributeMappingService.GetEffectiveMappingsAsync(model.AkeneoFamilyCode);

        var fallbackSourcesByMappingId =
            (await akeneoAttributeMappingService.GetAllFallbackSourcesAsync())
            .GroupBy(source => source.AttributeMappingId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<AkeneoAttributeMappingFallbackSource>)group
                    .OrderBy(source => source.DisplayOrder)
                    .ThenBy(source => source.Id)
                    .ToList());

        var specificationAttributes =
            await specificationAttributeService.GetAllSpecificationAttributesAsync();

        var productAttributes =
            await productAttributeService.GetAllProductAttributesAsync();

        model.Mappings.Clear();
        model.Warnings.Clear();

        model.NopTargetKeyMap = BuildNopTargetKeyMap();

        if (!akeneoAttributes.Any())
        {
            model.Warnings.Add(
                "No Akeneo attributes were found. Existing computed mappings remain available, but attribute tokens cannot be added until the Akeneo connection returns attributes.");
        }

        var referenceEntityAttributeCache =
            new Dictionary<string, IReadOnlyList<AkeneoReferenceEntityAttributeDefinition>>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var akeneoAttribute in akeneoAttributes
                     .OrderBy(GetAttributeGroup)
                     .ThenBy(attribute => attribute.Code))
        {
            var attributeMappings = existingAttributeMappings
                .Where(mapping =>
                    mapping.ValueModeId ==
                        (int)AkeneoAttributeMappingValueMode.SingleAttribute &&
                    string.Equals(
                        mapping.AkeneoAttributeCode,
                        akeneoAttribute.Code,
                        StringComparison.OrdinalIgnoreCase))
                .OrderBy(mapping => mapping.AkeneoReferenceEntityAttributeCode)
                .ThenBy(mapping => mapping.Id)
                .ToList();

            // A normal Akeneo attribute keeps the existing one-row behavior.
            // A reference-entity attribute gets one UI row per selected
            // reference-entity field, plus a blank starter row when it has not
            // been configured yet.
            if (!IsReferenceEntityType(akeneoAttribute))
            {
                var mappingModel = await CreateMappingModelAsync(
                    akeneoAttribute,
                    attributeMappings.FirstOrDefault(),
                    model.AkeneoFamilyCode,
                    specificationAttributes,
                    productAttributes,
                    referenceEntityAttributeCache,
                    fallbackSourcesByMappingId,
                    model.Warnings);

                model.Mappings.Add(mappingModel);
                continue;
            }

            if (!attributeMappings.Any())
                attributeMappings.Add(null);

            foreach (var existingAttributeMapping in attributeMappings)
            {
                var mappingModel = await CreateMappingModelAsync(
                    akeneoAttribute,
                    existingAttributeMapping,
                    model.AkeneoFamilyCode,
                    specificationAttributes,
                    productAttributes,
                    referenceEntityAttributeCache,
                    fallbackSourcesByMappingId,
                    model.Warnings);

                model.Mappings.Add(mappingModel);
            }
        }

        foreach (var computedMapping in existingAttributeMappings
                     .Where(mapping => mapping.ValueModeId ==
                         (int)AkeneoAttributeMappingValueMode.Template)
                     .OrderBy(mapping => mapping.Name)
                     .ThenBy(mapping => mapping.Id))
        {
            model.Mappings.Add(CreateComputedMappingModel(
                computedMapping,
                model.AkeneoFamilyCode,
                specificationAttributes,
                productAttributes));
        }

        PrepareFallbackSourceOptions(model);
        AddMappingWarnings(model);

        return model;
    }

    private async Task<AkeneoAttributeMappingModel> CreateMappingModelAsync(
        AkeneoAttributeDefinition akeneoAttribute,
        AkeneoAttributeMapping existingAttributeMapping,
        string selectedFamilyCode,
        IEnumerable<SpecificationAttribute> specificationAttributes,
        IEnumerable<ProductAttribute> productAttributes,
        IDictionary<string, IReadOnlyList<AkeneoReferenceEntityAttributeDefinition>> referenceEntityAttributeCache,
        IReadOnlyDictionary<int, IReadOnlyList<AkeneoAttributeMappingFallbackSource>> fallbackSourcesByMappingId,
        IList<string> warnings)
    {
        var defaultNopTargetType = targetTypeResolver.ResolveDefaultTargetType(
            akeneoAttribute);

        var isFamilyScope = !string.IsNullOrWhiteSpace(selectedFamilyCode);
        var isInherited =
            isFamilyScope &&
            existingAttributeMapping != null &&
            string.IsNullOrWhiteSpace(existingAttributeMapping.AkeneoFamilyCode);

        var mappingModel = new AkeneoAttributeMappingModel
        {
            Id = existingAttributeMapping?.Id ?? 0,
            MappingKey = existingAttributeMapping?.MappingKey,
            Name = existingAttributeMapping?.Name,
            ValueModeId = existingAttributeMapping?.ValueModeId ??
                (int)AkeneoAttributeMappingValueMode.SingleAttribute,
            ValueTemplate = existingAttributeMapping?.ValueTemplate,

            // Display-only values from Akeneo.
            AkeneoAttributeCode = akeneoAttribute.Code,
            AkeneoAttributeLabel = GetAttributeLabel(akeneoAttribute),
            AkeneoAttributeType = akeneoAttribute.Type,
            AkeneoAttributeGroup = GetAttributeGroup(akeneoAttribute),
            AkeneoAttributeGroupLabel = GetAttributeGroupLabel(akeneoAttribute),
            IsLocalizable = akeneoAttribute.Localizable,
            IsScopable = akeneoAttribute.Scopable,
            IsReferenceEntityAttribute = IsReferenceEntityType(akeneoAttribute),

            AkeneoReferenceEntityCode =
                existingAttributeMapping?.AkeneoReferenceEntityCode ??
                akeneoAttribute.ReferenceDataName,
            AkeneoReferenceEntityAttributeCode =
                existingAttributeMapping?.AkeneoReferenceEntityAttributeCode,

            // Persisted mapping values.
            AkeneoAttributeTypeId = existingAttributeMapping?.AkeneoAttributeTypeId
                ?? (int)targetTypeResolver.ResolveAkeneoAttributeType(akeneoAttribute),
            NopTargetTypeId = existingAttributeMapping?.NopTargetTypeId
                ?? (int)defaultNopTargetType,
            NopTargetKey = existingAttributeMapping?.NopTargetKey
                ?? targetTypeResolver.ResolveDefaultTargetKey(
                    akeneoAttribute,
                    defaultNopTargetType),
            Locale = existingAttributeMapping?.Locale,
            Channel = existingAttributeMapping?.Channel,
            TransformRuleJson = existingAttributeMapping?.TransformRuleJson,
            IsRequired = existingAttributeMapping?.IsRequired ?? false,
            EntityScopeId =
                AkeneoAttributeMappingScopeHelper.NormalizeConfiguredScopeId(
                    existingAttributeMapping?.EntityScopeId),
            NopTargetEntityId = existingAttributeMapping?.NopTargetEntityId,
            AkeneoFamilyCode = existingAttributeMapping?.AkeneoFamilyCode,
            IsInherited = isInherited,
            FallbackSources = GetFallbackSourceModels(
                existingAttributeMapping,
                fallbackSourcesByMappingId)
        };

        PrepareTargetTypeOptions(mappingModel, akeneoAttribute);
        PrepareNopTargetKeyOptions(mappingModel);
        PrepareSpecificationAttributeOptions(
            mappingModel,
            specificationAttributes);
        PrepareProductAttributeOptions(mappingModel, productAttributes);
        await PrepareReferenceEntityAttributeOptionsAsync(
            mappingModel,
            referenceEntityAttributeCache,
            warnings);

        return mappingModel;
    }


    private AkeneoAttributeMappingModel CreateComputedMappingModel(
        AkeneoAttributeMapping mapping,
        string selectedFamilyCode,
        IEnumerable<SpecificationAttribute> specificationAttributes,
        IEnumerable<ProductAttribute> productAttributes)
    {
        var isFamilyScope = !string.IsNullOrWhiteSpace(selectedFamilyCode);
        var isInherited =
            isFamilyScope &&
            string.IsNullOrWhiteSpace(mapping.AkeneoFamilyCode);

        var model = new AkeneoAttributeMappingModel
        {
            Id = mapping.Id,
            MappingKey = mapping.MappingKey,
            Name = mapping.Name,
            ValueModeId = (int)AkeneoAttributeMappingValueMode.Template,
            ValueTemplate = mapping.ValueTemplate,
            AkeneoFamilyCode = mapping.AkeneoFamilyCode,
            IsInherited = isInherited,
            AkeneoAttributeCode = null,
            AkeneoAttributeLabel = mapping.Name,
            AkeneoAttributeType = "computed_template",
            AkeneoAttributeGroup = "__computed",
            AkeneoAttributeGroupLabel = "Computed mappings",
            AkeneoAttributeTypeId = (int)AkeneoAttributeType.Text,
            NopTargetTypeId = mapping.NopTargetTypeId,
            NopTargetKey = mapping.NopTargetKey,
            NopTargetEntityId = mapping.NopTargetEntityId,
            Locale = mapping.Locale,
            Channel = mapping.Channel,
            TransformRuleJson = mapping.TransformRuleJson,
            IsRequired = mapping.IsRequired,
            EntityScopeId =
                AkeneoAttributeMappingScopeHelper.NormalizeConfiguredScopeId(
                    mapping.EntityScopeId),
            IsReferenceEntityAttribute = false,
            FallbackSources = new List<AkeneoAttributeMappingFallbackSourceModel>()
        };

        PrepareComputedTargetTypeOptions(model);
        PrepareNopTargetKeyOptions(model);
        PrepareSpecificationAttributeOptions(model, specificationAttributes);
        PrepareProductAttributeOptions(model, productAttributes);

        return model;
    }

    private static void PrepareComputedTargetTypeOptions(
        AkeneoAttributeMappingModel model)
    {
        model.AvailableTargetTypes.Clear();

        var targetTypes = new[]
        {
            NopTargetType.Ignore,
            NopTargetType.ProductField,
            NopTargetType.SpecificationAttribute,
            NopTargetType.ProductAttribute,
            NopTargetType.SeoField,
            NopTargetType.CustomProperty
        };

        foreach (var targetType in targetTypes)
        {
            model.AvailableTargetTypes.Add(new SelectListItem
            {
                Text = GetTargetTypeDisplayName(targetType),
                Value = ((int)targetType).ToString(),
                Selected = model.NopTargetTypeId == (int)targetType
            });
        }
    }


    private static IList<AkeneoAttributeMappingFallbackSourceModel>
        GetFallbackSourceModels(
            AkeneoAttributeMapping mapping,
            IReadOnlyDictionary<int, IReadOnlyList<AkeneoAttributeMappingFallbackSource>> fallbackSourcesByMappingId)
    {
        if (mapping == null ||
            !fallbackSourcesByMappingId.TryGetValue(mapping.Id, out var sources))
        {
            return new List<AkeneoAttributeMappingFallbackSourceModel>();
        }

        return sources
            .OrderBy(source => source.DisplayOrder)
            .ThenBy(source => source.Id)
            .Select(source => new AkeneoAttributeMappingFallbackSourceModel
            {
                AkeneoAttributeCode = source.AkeneoAttributeCode,
                AkeneoAttributeTypeId = source.AkeneoAttributeTypeId,
                AkeneoReferenceEntityCode = source.AkeneoReferenceEntityCode,
                AkeneoReferenceEntityAttributeCode =
                    source.AkeneoReferenceEntityAttributeCode,
                DisplayOrder = source.DisplayOrder
            })
            .ToList();
    }

    private static void PrepareFallbackSourceOptions(
        AkeneoAttributeMappingListModel model)
    {
        model.AvailableFallbackSources = model.Mappings
            .Where(mapping => !mapping.IsComputed)
            .Where(mapping =>
                !mapping.IsReferenceEntityAttribute ||
                !string.IsNullOrWhiteSpace(
                    mapping.AkeneoReferenceEntityAttributeCode))
            .Select(mapping =>
            {
                var referenceFieldLabel = mapping.IsReferenceEntityAttribute
                    ? mapping.AvailableReferenceEntityAttributes
                        .FirstOrDefault(option => string.Equals(
                            option.Value,
                            mapping.AkeneoReferenceEntityAttributeCode,
                            StringComparison.OrdinalIgnoreCase))
                        ?.Text
                    : null;

                var text = mapping.IsReferenceEntityAttribute
                    ? $"{mapping.AkeneoAttributeLabel} → {referenceFieldLabel ?? mapping.AkeneoReferenceEntityAttributeCode}"
                    : $"{mapping.AkeneoAttributeLabel} ({mapping.AkeneoAttributeCode})";

                return new AkeneoAttributeMappingSourceOptionModel
                {
                    Key = BuildSourceOptionKey(
                        mapping.AkeneoAttributeCode,
                        mapping.AkeneoReferenceEntityAttributeCode),
                    Text = text,
                    AkeneoAttributeCode = mapping.AkeneoAttributeCode,
                    AkeneoAttributeTypeId = mapping.AkeneoAttributeTypeId,
                    AkeneoReferenceEntityCode =
                        mapping.AkeneoReferenceEntityCode,
                    AkeneoReferenceEntityAttributeCode =
                        mapping.AkeneoReferenceEntityAttributeCode
                };
            })
            .GroupBy(option => option.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(option => option.Text)
            .ToList();
    }

    private static string BuildSourceOptionKey(
        string attributeCode,
        string referenceEntityAttributeCode)
    {
        var code = attributeCode?.Trim() ?? string.Empty;
        var field = referenceEntityAttributeCode?.Trim() ?? string.Empty;

        return string.IsNullOrWhiteSpace(field)
            ? code
            : $"{code}::{field}";
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
                Selected = model.NopTargetEntityId == specificationAttribute.Id
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
                Selected = model.NopTargetEntityId == productAttribute.Id
            });
        }
    }

    private async Task PrepareReferenceEntityAttributeOptionsAsync(
        AkeneoAttributeMappingModel model,
        IDictionary<string, IReadOnlyList<AkeneoReferenceEntityAttributeDefinition>> cache,
        IList<string> warnings)
    {
        model.AvailableReferenceEntityAttributes.Clear();

        if (!model.IsReferenceEntityAttribute)
            return;

        model.AvailableReferenceEntityAttributes.Add(new SelectListItem
        {
            Text = "Select reference entity field",
            Value = string.Empty
        });

        model.AvailableReferenceEntityAttributes.Add(new SelectListItem
        {
            Text = "Record code",
            Value = AkeneoReferenceEntityValueResolver.RecordCodeField,
            Selected = string.Equals(
                model.AkeneoReferenceEntityAttributeCode,
                AkeneoReferenceEntityValueResolver.RecordCodeField,
                StringComparison.OrdinalIgnoreCase)
        });

        var referenceEntityCode = model.AkeneoReferenceEntityCode?.Trim();

        if (string.IsNullOrWhiteSpace(referenceEntityCode))
        {
            warnings.Add(
                $"Akeneo reference entity attribute '{model.AkeneoAttributeCode}' did not expose a reference entity code.");
            return;
        }

        if (!cache.TryGetValue(referenceEntityCode, out var attributes))
        {
            try
            {
                attributes = await akeneoApiClient
                    .GetReferenceEntityAttributesAsync(referenceEntityCode);

                cache[referenceEntityCode] = attributes;
            }
            catch (Exception ex)
            {
                warnings.Add(
                    $"Could not load fields for reference entity '{referenceEntityCode}': {ex.Message}");
                return;
            }
        }

        foreach (var attribute in attributes
                     .Where(attribute => !string.IsNullOrWhiteSpace(attribute.Code))
                     .OrderBy(attribute => attribute.GetLabel())
                     .ThenBy(attribute => attribute.Code))
        {
            var typeSuffix = string.IsNullOrWhiteSpace(attribute.Type)
                ? string.Empty
                : $" · {attribute.Type}";

            model.AvailableReferenceEntityAttributes.Add(new SelectListItem
            {
                Text = $"{attribute.GetDisplayName()}{typeSuffix}",
                Value = attribute.Code,
                Selected = string.Equals(
                    model.AkeneoReferenceEntityAttributeCode,
                    attribute.Code,
                    StringComparison.OrdinalIgnoreCase)
            });
        }

        var selectedCode = model.AkeneoReferenceEntityAttributeCode?.Trim();
        if (!string.IsNullOrWhiteSpace(selectedCode) &&
            !model.AvailableReferenceEntityAttributes.Any(option =>
                string.Equals(option.Value, selectedCode, StringComparison.OrdinalIgnoreCase)))
        {
            model.AvailableReferenceEntityAttributes.Add(new SelectListItem
            {
                Text = $"Missing field ({selectedCode})",
                Value = selectedCode,
                Selected = true
            });
        }
    }

    private static bool IsReferenceEntityType(
        AkeneoAttributeDefinition attribute)
    {
        return string.Equals(
                   attribute.Type,
                   "akeneo_reference_entity",
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   attribute.Type,
                   "akeneo_reference_entity_collection",
                   StringComparison.OrdinalIgnoreCase);
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

        //if (!activeMappings.Any(mapping =>
        //        mapping.NopTargetTypeId == (int)NopTargetType.ProductField &&
        //        string.Equals(mapping.NopTargetKey, "Price", StringComparison.OrdinalIgnoreCase)))
        //{
        //    model.Warnings.Add("No Akeneo attribute is mapped to Product Price.");
        //}

        foreach (var mapping in activeMappings)
        {
            //if (mapping.NopTargetTypeId == (int)NopTargetType.SpecificationAttribute &&
            //    !mapping.NopTargetEntityId.HasValue)
            //{
            //    model.Warnings.Add(
            //        $"Akeneo attribute \"{mapping.AkeneoAttributeCode}\" is mapped as a specification attribute, but no nopCommerce specification attribute is selected.");
            //}

            if (mapping.NopTargetTypeId == (int)NopTargetType.ProductAttribute &&
                !mapping.NopTargetEntityId.HasValue)
            {
                model.Warnings.Add(
                    $"Mapping '{GetMappingDisplayName(mapping)}' is mapped as a product attribute, but no nopCommerce product attribute is selected.");
            }

            if (mapping.NopTargetTypeId == (int)NopTargetType.ProductField &&
                string.IsNullOrWhiteSpace(mapping.NopTargetKey))
            {
                model.Warnings.Add(
                    $"Mapping '{GetMappingDisplayName(mapping)}' is mapped as a product field, but no target key is selected.");
            }


            if (mapping.IsReferenceEntityAttribute &&
                string.IsNullOrWhiteSpace(mapping.AkeneoReferenceEntityAttributeCode))
            {
                model.Warnings.Add(
                    $"Akeneo reference entity attribute '{mapping.AkeneoAttributeCode}' has no reference entity field selected.");
            }
        }
    }

    private static string GetMappingDisplayName(
        AkeneoAttributeMappingModel mapping)
    {
        return mapping.IsComputed
            ? mapping.Name ?? "Computed mapping"
            : mapping.AkeneoAttributeCode ?? "Attribute mapping";
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