using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Services.Configuration;
using Nop.Services.Messages;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Controllers;

[AuthorizeAdmin]
[Area(AreaNames.ADMIN)]
[AutoValidateAntiforgeryToken]
public class AkeneoMappingController(
    IWorkContext workContext,
    IAkeneoAttributeMappingModelFactory attributeMappingModelFactory,
    IAkeneoAttributeMappingService attributeMappingService,
    IAkeneoApiClient akeneoApiClient,
    IAkeneoTargetTypeResolver targetTypeResolver,
    IAkeneoValueTemplateRenderer valueTemplateRenderer,
    INotificationService notificationService,
    IAkeneoNopEntityMappingService entityMappingService,
    IStoreContext storeContext,
    ISettingService settingService,
    IAkeneoCategoryMappingModelFactory categoryMappingModelFactory)
    : BasePluginController
{

    [HttpGet("admin/akeneo-connection/attribute-mappings")]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public async Task<IActionResult> AttributeMappings(string akeneoFamilyCode = null)
    {
        akeneoFamilyCode = string.IsNullOrWhiteSpace(akeneoFamilyCode)
            ? null
            : akeneoFamilyCode.Trim();

        var model = await attributeMappingModelFactory.PrepareAttributeMappingListModelAsync(akeneoFamilyCode);

        return View($"{AkeneoConnectionConstants.PathToPlugin}/Views/AttributeMapping/AttributeMappings.cshtml", model);
    }

    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    [HttpPost]
    public async Task<IActionResult> SaveAttributeMappingRow(
        AkeneoAttributeMappingModel model)
    {
        var errors = ValidateAttributeMappingRow(model);
        var valueMode = ResolveValueMode(model.ValueModeId, errors);
        var fallbackSources = valueMode ==
            AkeneoAttributeMappingValueMode.Template
                ? new List<AkeneoAttributeMappingFallbackSourceModel>()
                : ParseFallbackSources(
                    model.FallbackSourcesJson,
                    errors);

        if (valueMode == AkeneoAttributeMappingValueMode.Template)
        {
            await ValidateTemplateMappingAsync(model, errors);
        }
        else
        {
            await ValidateReferenceEntityMappingAsync(model, errors);
            await ValidateFallbackSourcesAsync(
                model,
                fallbackSources,
                errors);
            await ValidateMappingSlotUniquenessAsync(model, errors);
        }

        await ValidateTargetOverlapAsync(model, valueMode, errors);

        if (errors.Any())
        {
            return Json(new
            {
                success = false,
                errors
            });
        }

        var requestedFamilyCode =
            string.IsNullOrWhiteSpace(model.AkeneoFamilyCode)
                ? null
                : model.AkeneoFamilyCode.Trim();

        AkeneoAttributeMapping mapping = null;

        if (model.Id > 0)
        {
            mapping = await attributeMappingService
                .GetAkeneoAttributeMappingByIdAsync(model.Id);

            if (mapping == null)
            {
                return Json(new
                {
                    success = false,
                    errors = new[] { "The mapping could not be found." }
                });
            }

            var storedFamilyCode =
                string.IsNullOrWhiteSpace(mapping.AkeneoFamilyCode)
                    ? null
                    : mapping.AkeneoFamilyCode.Trim();

            if (!string.Equals(
                    storedFamilyCode,
                    requestedFamilyCode,
                    StringComparison.OrdinalIgnoreCase))
            {
                return Json(new
                {
                    success = false,
                    errors = new[]
                    {
                        "The mapping does not belong to the selected family scope."
                    }
                });
            }

            if (mapping.ValueModeId != (int)valueMode ||
                !MappingIdentityMatches(mapping, model, valueMode))
            {
                return Json(new
                {
                    success = false,
                    errors = new[]
                    {
                        "The saved mapping does not match the requested mapping identity."
                    }
                });
            }
        }

        var isReferenceEntityMapping =
            valueMode == AkeneoAttributeMappingValueMode.SingleAttribute &&
            IsReferenceEntityType(model.AkeneoAttributeTypeId);

        // A stable MappingKey makes computed mapping saves idempotent and also
        // identifies the family-scoped override slot for an inherited global
        // computed mapping.
        if (mapping == null &&
            valueMode == AkeneoAttributeMappingValueMode.Template &&
            !string.IsNullOrWhiteSpace(model.MappingKey))
        {
            mapping = (await attributeMappingService
                    .GetAllAkeneoAttributeMappingsAsync())
                .FirstOrDefault(candidate =>
                    candidate.ValueModeId ==
                        (int)AkeneoAttributeMappingValueMode.Template &&
                    string.Equals(
                        candidate.MappingKey,
                        model.MappingKey.Trim(),
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        NormalizeOptionalContext(candidate.AkeneoFamilyCode),
                        requestedFamilyCode,
                        StringComparison.OrdinalIgnoreCase));
        }

        // Normal Akeneo attributes retain one mapping row per scope. A
        // reference-entity attribute intentionally allows several rows, one per
        // selected reference-entity field. Computed mappings use MappingKey as
        // their stable override slot and are never looked up by attribute code.
        if (mapping == null &&
            valueMode == AkeneoAttributeMappingValueMode.SingleAttribute &&
            !isReferenceEntityMapping)
        {
            mapping = await attributeMappingService
                .GetAkeneoAttributeMappingByCodeAsync(
                    model.AkeneoAttributeCode,
                    requestedFamilyCode);
        }

        var isNew = mapping == null;

        mapping ??= new AkeneoAttributeMapping();

        mapping.ValueModeId = (int)valueMode;
        mapping.AkeneoFamilyCode = requestedFamilyCode;

        if (valueMode == AkeneoAttributeMappingValueMode.Template)
        {
            mapping.MappingKey = !string.IsNullOrWhiteSpace(model.MappingKey)
                ? model.MappingKey.Trim()
                : Guid.NewGuid().ToString("N");
            mapping.Name = model.Name.Trim();
            mapping.ValueTemplate = model.ValueTemplate.Trim();
            mapping.AkeneoAttributeCode = null;
            mapping.AkeneoAttributeTypeId = (int)AkeneoAttributeType.Text;
            mapping.AkeneoReferenceEntityCode = null;
            mapping.AkeneoReferenceEntityAttributeCode = null;
        }
        else
        {
            mapping.MappingKey = null;
            mapping.Name = null;
            mapping.ValueTemplate = null;
            mapping.AkeneoAttributeCode = model.AkeneoAttributeCode.Trim();
            mapping.AkeneoAttributeTypeId = model.AkeneoAttributeTypeId;

            if (isReferenceEntityMapping)
            {
                mapping.AkeneoReferenceEntityCode =
                    model.AkeneoReferenceEntityCode?.Trim();
                mapping.AkeneoReferenceEntityAttributeCode =
                    model.AkeneoReferenceEntityAttributeCode?.Trim();
            }
            else
            {
                mapping.AkeneoReferenceEntityCode = null;
                mapping.AkeneoReferenceEntityAttributeCode = null;
            }
        }

        mapping.NopTargetTypeId = model.NopTargetTypeId;
        mapping.NopTargetKey = model.NopTargetKey;

        // Only persist the entity id for target types that use one, so
        // switching targets cannot leave a stale destination id behind.
        mapping.NopTargetEntityId =
            model.NopTargetTypeId ==
                (int)NopTargetType.SpecificationAttribute ||
            model.NopTargetTypeId ==
                (int)NopTargetType.ProductAttribute
                ? model.NopTargetEntityId
                : null;

        mapping.Locale = NormalizeAkeneoLocale(model.Locale);
        mapping.Channel = NormalizeOptionalContext(model.Channel);
        mapping.TransformRuleJson = model.TransformRuleJson;
        mapping.IsRequired = model.IsRequired;
        mapping.EntityScopeId =
            AkeneoAttributeMappingScopeHelper.NormalizeConfiguredScopeId(
                model.EntityScopeId);

        if (isNew)
            await attributeMappingService.InsertAkeneoAttributeMappingAsync(mapping);
        else
            await attributeMappingService.UpdateAkeneoAttributeMappingAsync(mapping);

        await attributeMappingService.ReplaceFallbackSourcesAsync(
            mapping.Id,
            fallbackSources.Select((source, index) =>
                new AkeneoAttributeMappingFallbackSource
                {
                    AkeneoAttributeCode = source.AkeneoAttributeCode,
                    AkeneoAttributeTypeId = source.AkeneoAttributeTypeId,
                    AkeneoReferenceEntityCode =
                        source.AkeneoReferenceEntityCode,
                    AkeneoReferenceEntityAttributeCode =
                        source.AkeneoReferenceEntityAttributeCode,
                    DisplayOrder = index
                }));

        return Json(new
        {
            success = true,
            id = mapping.Id,
            mappingKey = mapping.MappingKey,
            message = "Mapping saved."
        });
    }


    private static AkeneoAttributeMappingValueMode ResolveValueMode(
        int valueModeId,
        IList<string> errors)
    {
        if (Enum.IsDefined(
                typeof(AkeneoAttributeMappingValueMode),
                valueModeId))
        {
            return (AkeneoAttributeMappingValueMode)valueModeId;
        }

        errors.Add("Invalid mapping value mode.");
        return AkeneoAttributeMappingValueMode.SingleAttribute;
    }

    private async Task ValidateTemplateMappingAsync(
        AkeneoAttributeMappingModel model,
        IList<string> errors)
    {
        if (string.IsNullOrWhiteSpace(model.ValueTemplate))
            return;

        var validation = valueTemplateRenderer.Validate(
            model.ValueTemplate);

        foreach (var error in validation.Errors)
            errors.Add(error);

        if (!validation.Success ||
            validation.ReferencedAttributeCodes.Count == 0)
        {
            return;
        }

        try
        {
            var availableAttributes = await akeneoApiClient
                .GetAttributesAsync();

            var availableCodes = availableAttributes
                .Where(attribute =>
                    !string.IsNullOrWhiteSpace(attribute.Code))
                .Select(attribute => attribute.Code.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(model.AkeneoFamilyCode))
            {
                var family = await akeneoApiClient.GetFamilyByCodeAsync(
                    model.AkeneoFamilyCode.Trim());

                if (family == null)
                {
                    errors.Add(
                        $"Akeneo family '{model.AkeneoFamilyCode}' could not be found.");
                    return;
                }

                availableCodes.IntersectWith(
                    family.Attributes ?? new List<string>());
            }

            foreach (var attributeCode in
                     validation.ReferencedAttributeCodes)
            {
                if (!availableCodes.Contains(attributeCode))
                {
                    errors.Add(
                        $"Template token '{{{attributeCode}}}' refers to an Akeneo attribute that is not available in this mapping scope.");
                }
            }
        }
        catch (Exception ex)
        {
            errors.Add(
                $"Could not validate template attributes against Akeneo: {ex.Message}");
        }
    }

    private async Task ValidateTargetOverlapAsync(
        AkeneoAttributeMappingModel model,
        AkeneoAttributeMappingValueMode valueMode,
        IList<string> errors)
    {
        if (!Enum.IsDefined(typeof(NopTargetType), model.NopTargetTypeId))
            return;

        var targetType = (NopTargetType)model.NopTargetTypeId;

        if (targetType is not (
                NopTargetType.ProductField or
                NopTargetType.SeoField or
                NopTargetType.CustomProperty))
        {
            return;
        }

        var familyCode = NormalizeOptionalContext(
            model.AkeneoFamilyCode);
        var effectiveMappings = await attributeMappingService
            .GetEffectiveMappingsAsync(familyCode);
        var requestedScope =
            AkeneoAttributeMappingScopeHelper.NormalizeConfiguredScopeId(
                model.EntityScopeId);

        var conflictingMapping = effectiveMappings.FirstOrDefault(mapping =>
            mapping.Id != model.Id &&
            !IsSameLogicalSlot(mapping, model, valueMode) &&
            mapping.NopTargetTypeId == model.NopTargetTypeId &&
            string.Equals(
                mapping.NopTargetKey?.Trim(),
                model.NopTargetKey?.Trim(),
                StringComparison.OrdinalIgnoreCase) &&
            (AkeneoAttributeMappingScopeHelper
                 .NormalizeConfiguredScopeId(mapping.EntityScopeId) &
             requestedScope) != 0);

        if (conflictingMapping == null)
            return;

        var conflictingName = conflictingMapping.ValueModeId ==
            (int)AkeneoAttributeMappingValueMode.Template
                ? conflictingMapping.Name ?? "Computed mapping"
                : AkeneoMappingHelper.GetSourceDisplayName(
                    conflictingMapping);

        errors.Add(
            $"The destination '{targetType}: {model.NopTargetKey}' already has an overlapping mapping from '{conflictingName}'. Adjust the Apply mapping to switches or edit the existing mapping instead.");
    }

    private static bool IsSameLogicalSlot(
        AkeneoAttributeMapping mapping,
        AkeneoAttributeMappingModel model,
        AkeneoAttributeMappingValueMode valueMode)
    {
        if (mapping.ValueModeId != (int)valueMode)
            return false;

        if (valueMode == AkeneoAttributeMappingValueMode.Template)
        {
            return !string.IsNullOrWhiteSpace(model.MappingKey) &&
                string.Equals(
                    mapping.MappingKey,
                    model.MappingKey,
                    StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(
                   mapping.AkeneoAttributeCode,
                   model.AkeneoAttributeCode,
                   StringComparison.OrdinalIgnoreCase) &&
               string.Equals(
                   mapping.AkeneoReferenceEntityAttributeCode,
                   model.AkeneoReferenceEntityAttributeCode,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool MappingIdentityMatches(
        AkeneoAttributeMapping mapping,
        AkeneoAttributeMappingModel model,
        AkeneoAttributeMappingValueMode valueMode)
    {
        if (valueMode == AkeneoAttributeMappingValueMode.Template)
        {
            return string.Equals(
                mapping.MappingKey,
                model.MappingKey,
                StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(
            mapping.AkeneoAttributeCode,
            model.AkeneoAttributeCode,
            StringComparison.OrdinalIgnoreCase);
    }


    private static IList<AkeneoAttributeMappingFallbackSourceModel>
        ParseFallbackSources(
            string json,
            IList<string> errors)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<AkeneoAttributeMappingFallbackSourceModel>();

        try
        {
            return JsonSerializer.Deserialize<
                       List<AkeneoAttributeMappingFallbackSourceModel>>(
                       json,
                       new JsonSerializerOptions
                       {
                           PropertyNameCaseInsensitive = true
                       }) ??
                   new List<AkeneoAttributeMappingFallbackSourceModel>();
        }
        catch (JsonException)
        {
            errors.Add("Fallback source configuration is not valid JSON.");
            return new List<AkeneoAttributeMappingFallbackSourceModel>();
        }
    }

    private async Task ValidateFallbackSourcesAsync(
        AkeneoAttributeMappingModel primaryMapping,
        IList<AkeneoAttributeMappingFallbackSourceModel> fallbackSources,
        IList<string> errors)
    {
        var seenSources = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        var primaryKey = BuildSourceKey(
            primaryMapping.AkeneoAttributeCode,
            primaryMapping.AkeneoReferenceEntityAttributeCode);

        for (var index = 0; index < fallbackSources.Count; index++)
        {
            var source = fallbackSources[index];
            source.DisplayOrder = index;
            source.AkeneoAttributeCode =
                NormalizeOptionalContext(source.AkeneoAttributeCode);
            source.AkeneoReferenceEntityCode =
                NormalizeOptionalContext(source.AkeneoReferenceEntityCode);
            source.AkeneoReferenceEntityAttributeCode =
                NormalizeOptionalContext(
                    source.AkeneoReferenceEntityAttributeCode);

            if (string.IsNullOrWhiteSpace(source.AkeneoAttributeCode))
            {
                errors.Add($"Fallback source {index + 1} must select an Akeneo attribute.");
                continue;
            }

            var sourceKey = BuildSourceKey(
                source.AkeneoAttributeCode,
                source.AkeneoReferenceEntityAttributeCode);

            if (string.Equals(
                    primaryKey,
                    sourceKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(
                    $"Fallback source '{source.AkeneoAttributeCode}' duplicates the primary source.");
                continue;
            }

            if (!seenSources.Add(sourceKey))
            {
                errors.Add(
                    $"Fallback source '{source.AkeneoAttributeCode}' is selected more than once.");
                continue;
            }

            try
            {
                var attribute = await akeneoApiClient.GetAttributeByCodeAsync(
                    source.AkeneoAttributeCode);

                if (attribute == null)
                {
                    errors.Add(
                        $"Fallback Akeneo attribute '{source.AkeneoAttributeCode}' could not be found.");
                    continue;
                }

                var actualType =
                    targetTypeResolver.ResolveAkeneoAttributeType(attribute);
                source.AkeneoAttributeTypeId = (int)actualType;

                if (!IsReferenceEntityType(source.AkeneoAttributeTypeId))
                {
                    source.AkeneoReferenceEntityCode = null;
                    source.AkeneoReferenceEntityAttributeCode = null;
                    continue;
                }

                source.AkeneoReferenceEntityCode =
                    NormalizeOptionalContext(attribute.ReferenceDataName) ??
                    source.AkeneoReferenceEntityCode;

                if (string.IsNullOrWhiteSpace(
                        source.AkeneoReferenceEntityAttributeCode))
                {
                    errors.Add(
                        $"Fallback reference-entity attribute '{source.AkeneoAttributeCode}' requires a field selection.");
                    continue;
                }

                if (string.Equals(
                        source.AkeneoReferenceEntityAttributeCode,
                        AkeneoReferenceEntityValueResolver.RecordCodeField,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(
                        source.AkeneoReferenceEntityCode))
                {
                    errors.Add(
                        $"Fallback attribute '{source.AkeneoAttributeCode}' does not identify its reference entity.");
                    continue;
                }

                var fields = await akeneoApiClient
                    .GetReferenceEntityAttributesAsync(
                        source.AkeneoReferenceEntityCode);

                if (!fields.Any(field => string.Equals(
                        field.Code,
                        source.AkeneoReferenceEntityAttributeCode,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    errors.Add(
                        $"Reference entity field '{source.AkeneoReferenceEntityAttributeCode}' was not found for fallback attribute '{source.AkeneoAttributeCode}'.");
                }
            }
            catch (Exception ex)
            {
                errors.Add(
                    $"Could not validate fallback source '{source.AkeneoAttributeCode}': {ex.Message}");
            }
        }
    }

    private static string BuildSourceKey(
        string attributeCode,
        string referenceEntityAttributeCode)
    {
        var code = attributeCode?.Trim() ?? string.Empty;
        var field = referenceEntityAttributeCode?.Trim() ?? string.Empty;

        return string.IsNullOrWhiteSpace(field)
            ? code
            : $"{code}::{field}";
    }

    private async Task ValidateReferenceEntityMappingAsync(
        AkeneoAttributeMappingModel model,
        IList<string> errors)
    {
        if (model.ValueModeId ==
                (int)AkeneoAttributeMappingValueMode.Template ||
            string.IsNullOrWhiteSpace(model.AkeneoAttributeCode))
        {
            return;
        }

        try
        {
            var attribute = await akeneoApiClient.GetAttributeByCodeAsync(
                model.AkeneoAttributeCode);

            if (attribute == null)
            {
                errors.Add(
                    $"Akeneo attribute '{model.AkeneoAttributeCode}' could not be found.");
                return;
            }

            var actualType = targetTypeResolver.ResolveAkeneoAttributeType(attribute);
            model.AkeneoAttributeTypeId = (int)actualType;

            if (!IsReferenceEntityType(model.AkeneoAttributeTypeId))
            {
                model.AkeneoReferenceEntityCode = null;
                model.AkeneoReferenceEntityAttributeCode = null;
                return;
            }

            var referenceEntityCode = !string.IsNullOrWhiteSpace(attribute.ReferenceDataName)
                ? attribute.ReferenceDataName.Trim()
                : model.AkeneoReferenceEntityCode?.Trim();

            model.AkeneoReferenceEntityCode = referenceEntityCode;

            if (string.IsNullOrWhiteSpace(referenceEntityCode))
            {
                errors.Add(
                    "Akeneo did not identify the reference entity linked by this attribute.");
                return;
            }

            var selectedField = model.AkeneoReferenceEntityAttributeCode?.Trim();
            if (string.IsNullOrWhiteSpace(selectedField))
            {
                errors.Add("Reference Entity Field is required for this Akeneo attribute.");
                return;
            }

            if (string.Equals(
                    selectedField,
                    AkeneoReferenceEntityValueResolver.RecordCodeField,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var fields = await akeneoApiClient
                .GetReferenceEntityAttributesAsync(referenceEntityCode);

            if (!fields.Any(field =>
                    string.Equals(
                        field.Code,
                        selectedField,
                        StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add(
                    $"Reference entity field '{selectedField}' does not exist on '{referenceEntityCode}'.");
            }
        }
        catch (Exception ex)
        {
            errors.Add(
                $"Could not validate the Akeneo reference entity field: {ex.Message}");
        }
    }

    private async Task ValidateMappingSlotUniquenessAsync(
        AkeneoAttributeMappingModel model,
        IList<string> errors)
    {
        if (string.IsNullOrWhiteSpace(model.AkeneoAttributeCode) ||
            !IsReferenceEntityType(model.AkeneoAttributeTypeId))
        {
            return;
        }

        var selectedField =
            model.AkeneoReferenceEntityAttributeCode?.Trim();

        // Field validation reports the missing selection. There is no slot to
        // compare until a field has been chosen.
        if (string.IsNullOrWhiteSpace(selectedField))
            return;

        var familyCode = string.IsNullOrWhiteSpace(model.AkeneoFamilyCode)
            ? null
            : model.AkeneoFamilyCode.Trim();

        var mappings = await attributeMappingService
            .GetAkeneoAttributeMappingsByCodeAsync(
                model.AkeneoAttributeCode,
                familyCode);

        var duplicate = mappings.Any(mapping =>
            mapping.Id != model.Id &&
            string.Equals(
                mapping.AkeneoReferenceEntityAttributeCode,
                selectedField,
                StringComparison.OrdinalIgnoreCase));

        if (duplicate)
        {
            errors.Add(
                $"Reference entity field '{selectedField}' already has a mapping in this scope. Edit that mapping instead of adding a duplicate.");
        }
    }


    private static string NormalizeAkeneoLocale(string value)
    {
        var normalized = NormalizeOptionalContext(value);
        return normalized?.Replace('-', '_');
    }

    private static string NormalizeOptionalContext(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static bool IsReferenceEntityType(int attributeTypeId)
    {
        return attributeTypeId == (int)AkeneoAttributeType.ReferenceEntity ||
               attributeTypeId == (int)AkeneoAttributeType.ReferenceEntityCollection;
    }

    private static IList<string> ValidateAttributeMappingRow(
        AkeneoAttributeMappingModel model)
    {
        var errors = new List<string>();

        if (!Enum.IsDefined(
                typeof(AkeneoAttributeMappingValueMode),
                model.ValueModeId))
        {
            errors.Add("Invalid mapping value mode.");
        }
        else if (model.ValueModeId ==
                 (int)AkeneoAttributeMappingValueMode.Template)
        {
            if (string.IsNullOrWhiteSpace(model.Name))
                errors.Add("Computed mapping name is required.");
            else if (model.Name.Trim().Length > 255)
                errors.Add("Computed mapping name cannot exceed 255 characters.");

            if (string.IsNullOrWhiteSpace(model.ValueTemplate))
                errors.Add("Value template is required.");
            else if (model.ValueTemplate.Length > 4000)
                errors.Add("Value template cannot exceed 4000 characters.");

            if (!string.IsNullOrWhiteSpace(model.MappingKey) &&
                model.MappingKey.Trim().Length > 100)
            {
                errors.Add("Computed mapping key cannot exceed 100 characters.");
            }
        }
        else if (string.IsNullOrWhiteSpace(model.AkeneoAttributeCode))
        {
            errors.Add("Akeneo attribute code is required.");
        }

        if (!Enum.IsDefined(typeof(NopTargetType), model.NopTargetTypeId))
            errors.Add("Invalid nopCommerce target type.");

        if (!AkeneoAttributeMappingScopeHelper.IsValidConfiguredScopeId(
                model.EntityScopeId))
        {
            errors.Add(
                "Select at least one valid destination under Apply mapping to.");
        }

        if (!string.IsNullOrWhiteSpace(model.TransformRuleJson))
        {
            try
            {
                JsonDocument.Parse(model.TransformRuleJson);
            }
            catch (JsonException)
            {
                errors.Add("Transform Rule JSON is not valid JSON.");
            }
        }

        var targetType = (NopTargetType)model.NopTargetTypeId;

        if ((targetType == NopTargetType.ProductField ||
             targetType == NopTargetType.SeoField ||
             targetType == NopTargetType.CustomProperty) &&
            string.IsNullOrWhiteSpace(model.NopTargetKey))
        {
            errors.Add("Target Key is required for the selected target type.");
        }

        if (targetType == NopTargetType.SpecificationAttribute &&
            (!model.NopTargetEntityId.HasValue || model.NopTargetEntityId.Value <= 0))
        {
            errors.Add("Specification Attribute is required when Target Type is Specification Attribute.");
        }

        if (targetType == NopTargetType.ProductAttribute &&
            (!model.NopTargetEntityId.HasValue || model.NopTargetEntityId <= 0))
        {
            errors.Add("Product Attribute is required when Target Type is Product Attribute.");
        }

        return errors;
    }

    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    [HttpPost]
    public async Task<IActionResult> DeleteAttributeMappingRow(
        int id,
        string akeneoFamilyCode,
        string akeneoAttributeCode,
        string mappingKey)
    {
        if (id <= 0)
        {
            return Json(new
            {
                success = false,
                errors = new[] { "A saved mapping is required." }
            });
        }

        var mapping = await attributeMappingService
            .GetAkeneoAttributeMappingByIdAsync(id);

        if (mapping == null)
        {
            return Json(new
            {
                success = false,
                errors = new[] { "The mapping could not be found." }
            });
        }

        var requestedFamilyCode = string.IsNullOrWhiteSpace(akeneoFamilyCode)
            ? null
            : akeneoFamilyCode.Trim();
        var storedFamilyCode = string.IsNullOrWhiteSpace(mapping.AkeneoFamilyCode)
            ? null
            : mapping.AkeneoFamilyCode.Trim();

        var identityMatches = mapping.ValueModeId ==
            (int)AkeneoAttributeMappingValueMode.Template
                ? string.Equals(
                    mapping.MappingKey,
                    mappingKey,
                    StringComparison.OrdinalIgnoreCase)
                : string.Equals(
                    mapping.AkeneoAttributeCode,
                    akeneoAttributeCode,
                    StringComparison.OrdinalIgnoreCase);

        if (!string.Equals(
                storedFamilyCode,
                requestedFamilyCode,
                StringComparison.OrdinalIgnoreCase) ||
            !identityMatches)
        {
            return Json(new
            {
                success = false,
                errors = new[]
                {
                    "The mapping does not belong to the requested attribute and scope."
                }
            });
        }

        await attributeMappingService.DeleteAkeneoAttributeMappingAsync(mapping);

        AkeneoAttributeMapping replacementMapping = null;

        // When a family override is removed, the matching global mapping may
        // immediately become effective. Return it so Vue can update the row in
        // place without reloading the page.
        if (!string.IsNullOrWhiteSpace(requestedFamilyCode))
        {
            var effectiveMappings = await attributeMappingService
                .GetEffectiveMappingsAsync(requestedFamilyCode);

            replacementMapping = effectiveMappings.FirstOrDefault(candidate =>
                MappingSlotsMatch(candidate, mapping));
        }

        var fallbackSources = replacementMapping == null
            ? Array.Empty<AkeneoAttributeMappingFallbackSource>()
            : (await attributeMappingService.GetAllFallbackSourcesAsync())
                .Where(source =>
                    source.AttributeMappingId == replacementMapping.Id)
                .OrderBy(source => source.DisplayOrder)
                .ThenBy(source => source.Id)
                .ToArray();

        return Json(new
        {
            success = true,
            replacementMapping = replacementMapping == null
                ? null
                : new
                {
                    id = replacementMapping.Id,
                    mappingKey = replacementMapping.MappingKey ?? string.Empty,
                    name = replacementMapping.Name ?? string.Empty,
                    valueModeId = replacementMapping.ValueModeId,
                    valueTemplate = replacementMapping.ValueTemplate ?? string.Empty,
                    isComputed = replacementMapping.ValueModeId ==
                        (int)AkeneoAttributeMappingValueMode.Template,
                    akeneoFamilyCode =
                        replacementMapping.AkeneoFamilyCode ?? string.Empty,
                    isInherited = true,
                    referenceEntityAttributeCode =
                        replacementMapping.AkeneoReferenceEntityAttributeCode ??
                        string.Empty,
                    nopTargetTypeId =
                        replacementMapping.NopTargetTypeId.ToString(),
                    nopTargetKey = replacementMapping.NopTargetKey ?? string.Empty,
                    nopTargetEntityId =
                        replacementMapping.NopTargetEntityId?.ToString() ??
                        string.Empty,
                    isRequired = replacementMapping.IsRequired,
                    entityScopeId = replacementMapping.EntityScopeId,
                    fallbackSources = fallbackSources.Select(source => new
                    {
                        akeneoAttributeCode = source.AkeneoAttributeCode,
                        akeneoAttributeTypeId = source.AkeneoAttributeTypeId,
                        akeneoReferenceEntityCode =
                            source.AkeneoReferenceEntityCode ?? string.Empty,
                        akeneoReferenceEntityAttributeCode =
                            source.AkeneoReferenceEntityAttributeCode ??
                            string.Empty,
                        displayOrder = source.DisplayOrder
                    }),
                    transformRuleJson =
                        replacementMapping.TransformRuleJson ?? string.Empty
                }
        });
    }

    private static bool MappingSlotsMatch(
        AkeneoAttributeMapping left,
        AkeneoAttributeMapping right)
    {
        if (left == null || right == null ||
            left.ValueModeId != right.ValueModeId)
        {
            return false;
        }

        if (left.ValueModeId ==
            (int)AkeneoAttributeMappingValueMode.Template)
        {
            return string.Equals(
                NormalizeMappingSlotPart(left.MappingKey),
                NormalizeMappingSlotPart(right.MappingKey),
                StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(
                   NormalizeMappingSlotPart(left.AkeneoAttributeCode),
                   NormalizeMappingSlotPart(right.AkeneoAttributeCode),
                   StringComparison.OrdinalIgnoreCase) &&
               string.Equals(
                   NormalizeMappingSlotPart(
                       left.AkeneoReferenceEntityAttributeCode),
                   NormalizeMappingSlotPart(
                       right.AkeneoReferenceEntityAttributeCode),
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeMappingSlotPart(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();
    }

    [HttpGet("admin/akeneo-connection/category-mappings")]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public async Task<IActionResult> CategoryMappings()
    {
        var model = await categoryMappingModelFactory.PrepareCategoryMappingListModelAsync();
        return View("~/Plugins/Apt.Misc.AkeneoConnection/Views/CategoryMappings.cshtml", model);
    }

    [HttpPost("admin/akeneo-connection/category-mappings")]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public async Task<IActionResult> SaveCategoryMapping(AkeneoCategoryMappingModel model)
    {
        

        if (string.IsNullOrWhiteSpace(model.AkeneoCode))
        {
            return Json(new
            {
                success = false,
                message = "Akeneo category code is required."
            });
        }

        if (model.NopCategoryId <= 0)
        {
            return Json(new
            {
                success = false,
                message = "Please select a nopCommerce category."
            });
        }

        await entityMappingService.UpsertAkeneoNopEntityMappingAsync(
            AkeneoEntityType.Category,
            model.AkeneoCode,
            null,
            NopEntityType.Category,
            model.NopCategoryId);

        return Json(new
        {
            success = true,
            message = "Category mapping saved."
        });
    }

    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    [HttpPost]
    public async Task<IActionResult> DeleteCategoryMapping(string akeneoCode)
    {
       

        if (string.IsNullOrWhiteSpace(akeneoCode))
        {
            return Json(new
            {
                success = false,
                message = "Akeneo category code is required."
            });
        }

        await entityMappingService.DeleteAkeneoNopEntityMappingAsync(
            AkeneoEntityType.Category,
            akeneoCode,
            NopEntityType.Category);

        return Json(new
        {
            success = true,
            message = "Category mapping cleared."
        });
    }

    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    [HttpPost]
    public async Task<IActionResult> DeleteAttributeMappingOverride(
        int id,
        string akeneoFamilyCode,
        string akeneoAttributeCode)
    {
        if (id <= 0 || string.IsNullOrWhiteSpace(akeneoFamilyCode))
        {
            return Json(new
            {
                success = false,
                errors = new[] { "A family override is required." }
            });
        }

        var mapping = await attributeMappingService
            .GetAkeneoAttributeMappingByIdAsync(id);

        if (mapping == null)
        {
            return Json(new
            {
                success = false,
                errors = new[] { "The mapping could not be found." }
            });
        }

        if (string.IsNullOrWhiteSpace(mapping.AkeneoFamilyCode) ||
            !string.Equals(
                mapping.AkeneoFamilyCode,
                akeneoFamilyCode,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                mapping.AkeneoAttributeCode,
                akeneoAttributeCode,
                StringComparison.OrdinalIgnoreCase))
        {
            return Json(new
            {
                success = false,
                errors = new[]
                {
                    "The requested mapping is not a family override."
                }
            });
        }

        await attributeMappingService
            .DeleteAkeneoAttributeMappingAsync(mapping);

        return Json(new
        {
            success = true
        });
    }
}
