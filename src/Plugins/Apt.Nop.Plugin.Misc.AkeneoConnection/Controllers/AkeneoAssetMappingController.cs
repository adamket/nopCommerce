using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Extensions;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Controllers;

[AuthorizeAdmin]
[Area(AreaNames.ADMIN)]
[AutoValidateAntiforgeryToken]
public sealed class AkeneoAssetMappingController(
    IAkeneoAssetMappingModelFactory modelFactory,
    IAkeneoAssetMappingService mappingService,
    IAkeneoApiClient apiClient,
    IAkeneoValueTemplateRenderer templateRenderer)
    : BasePluginController
{

    

    private IActionResult JsonWeb(object value) =>
        Content(JsonSerializer.Serialize(value, WebJsonOptions), "application/json");

    [HttpGet("admin/akeneo-connection/asset-mappings")]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public async Task<IActionResult> List(string akeneoFamilyCode = null)
    {
        var model = await modelFactory.PrepareListModelAsync(akeneoFamilyCode);
        return View(
            $"{AkeneoConnectionConstants.PathToPlugin}/Views/AssetMapping/AssetMappings.cshtml",
            model);
    }

    [HttpGet]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    [HttpGet]
    public async Task<IActionResult> AssetFamilyAttributes(
        string assetFamilyCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(assetFamilyCode))
        {
            return JsonWeb(new
            {
                success = false,
                errors = new[]
                {
                    "An asset family code is required."
                }
            });
        }

        try
        {
            var family = await apiClient.GetAssetFamilyByCodeAsync(
                assetFamilyCode.Trim(),
                cancellationToken);

            var attributes =
                await apiClient.GetAssetFamilyAttributesAsync(
                    assetFamilyCode.Trim(),
                    cancellationToken);

            var responseAttributes = attributes
                .Select(attribute => new
                {
                    code = attribute.Code,
                    label = ResolveAssetAttributeLabel(attribute),
                    type = attribute.Type,
                    mediaType = attribute.MediaType,
                    isMedia = IsMediaAttributeType(attribute.Type)
                })
                .OrderBy(attribute => attribute.label)
                .ThenBy(attribute => attribute.code)
                .ToList();

            return JsonWeb(new
            {
                success = true,
                mainMediaAttributeCode =
                    family?.AttributeAsMainMedia ?? string.Empty,
                attributes = responseAttributes
            });
        }
        catch (Exception exception)
        {
            return JsonWeb(new
            {
                success = false,
                errors = new[]
                {
                    exception.Message
                }
            });
        }
    }

    [HttpPost]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public async Task<IActionResult> Save(AkeneoAssetMappingModel model)
    {
        var errors = await ValidateAsync(model);
        if (errors.Count > 0)
            return JsonWeb(new { success = false, errors });

        var selectedFamily = model.AkeneoFamilyCode.TrimOrNull();
        AkeneoAssetMapping mapping = null;
        if (model.Id > 0)
        {
            mapping = await mappingService.GetByIdAsync(model.Id);
            if (mapping == null)
                errors.Add("The asset mapping could not be found.");
            else if (!string.Equals(
                         mapping.AkeneoFamilyCode.TrimOrNull(),
                         selectedFamily,
                         StringComparison.OrdinalIgnoreCase))
                errors.Add("The mapping does not belong to the selected family scope.");
        }

        if (errors.Count > 0)
            return JsonWeb(new { success = false, errors });

        if (mapping == null && !string.IsNullOrWhiteSpace(model.MappingKey))
        {
            mapping = (await mappingService.GetAllAsync())
                .FirstOrDefault(candidate =>
                    string.Equals(candidate.MappingKey, model.MappingKey.Trim(), StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(candidate.AkeneoFamilyCode.TrimOrNull(), selectedFamily, StringComparison.OrdinalIgnoreCase));
        }

        mapping ??= new AkeneoAssetMapping();
        var isNew = mapping.Id == 0;
        var now = DateTime.UtcNow;

        mapping.MappingKey = string.IsNullOrWhiteSpace(model.MappingKey)
            ? Guid.NewGuid().ToString("N")
            : model.MappingKey.Trim();
        mapping.Name = model.Name.Trim();
        mapping.Enabled = model.Enabled;
        mapping.AkeneoFamilyCode = selectedFamily;
        mapping.SourceTypeId = model.SourceTypeId;
        mapping.SourceAttributeCode = model.SourceAttributeCode.Trim();
        mapping.FallbackSourceAttributeCode =
            model.FallbackSourceAttributeCode.TrimOrNull();
        mapping.AssetFamilyCode = model.AssetFamilyCode.TrimOrNull();
        mapping.AssetMediaAttributeCode = model.AssetMediaAttributeCode.TrimOrNull();
        mapping.AssetMediaType = model.AssetMediaType.TrimOrNull();
        mapping.DestinationTypeId = model.DestinationTypeId;
        mapping.StorageModeId = model.StorageModeId;
        mapping.EntityScopeId = AkeneoAttributeMappingScopeHelper
            .NormalizeConfiguredScopeId(model.EntityScopeId);
        mapping.RoleAttributeCode = model.RoleAttributeCode.TrimOrNull();
        mapping.RoleValuesCsv = model.RoleValuesCsv.TrimOrNull();
        mapping.SortOrderAttributeCode = model.SortOrderAttributeCode.TrimOrNull();
        mapping.AltTextTemplate = model.AltTextTemplate.TrimOrNull();
        mapping.TitleTextTemplate = model.TitleTextTemplate.TrimOrNull();
        mapping.SeoFilenameTemplate = model.SeoFilenameTemplate.TrimOrNull();
        mapping.CustomPropertyKey = model.CustomPropertyKey.TrimOrNull();
        mapping.DisplayOrder = Math.Max(0, model.DisplayOrder);
        mapping.MaxAssets = model.MaxAssets is > 0 and <= 100 ? model.MaxAssets : 20;
        mapping.UpdatedOnUtc = now;

        if (isNew)
        {
            mapping.CreatedOnUtc = now;
            await mappingService.InsertAsync(mapping);
        }
        else
        {
            await mappingService.UpdateAsync(mapping);
        }

        var savedModel = modelFactory.PrepareMappingModel(
            mapping,
            selectedFamily);

        return JsonWeb(new
        {
            success = true,
            mapping = savedModel
        });
    }

    [HttpPost]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public async Task<IActionResult> Delete(
        int id,
        string akeneoFamilyCode = null)
    {
        var mapping = await mappingService.GetByIdAsync(id);
        if (mapping == null)
            return JsonWeb(new { success = false, errors = new[] { "The asset mapping could not be found." } });

        var selectedFamily = akeneoFamilyCode.TrimOrNull();
        if (!string.Equals(
                mapping.AkeneoFamilyCode.TrimOrNull(),
                selectedFamily,
                StringComparison.OrdinalIgnoreCase))
        {
            return JsonWeb(new { success = false, errors = new[] { "The mapping does not belong to the selected family scope." } });
        }

        var mappingKey = mapping.MappingKey;
        await mappingService.DeleteAsync(mapping);

        AkeneoAssetMappingModel replacement = null;
        if (selectedFamily != null)
        {
            var inherited = (await mappingService.GetAllAsync())
                .FirstOrDefault(candidate =>
                    string.IsNullOrWhiteSpace(candidate.AkeneoFamilyCode) &&
                    string.Equals(candidate.MappingKey, mappingKey, StringComparison.OrdinalIgnoreCase));
            if (inherited != null)
                replacement = modelFactory.PrepareMappingModel(inherited, selectedFamily);
        }
        return JsonWeb(new { success = true, replacement });
    }

    private async Task<List<string>> ValidateAsync(AkeneoAssetMappingModel model)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(model.Name))
            errors.Add("Mapping name is required.");
        if (string.IsNullOrWhiteSpace(model.SourceAttributeCode))
            errors.Add("Select an Akeneo media or asset collection attribute.");
        if (!AkeneoAttributeMappingScopeHelper.IsValidConfiguredScopeId(model.EntityScopeId))
            errors.Add("Select at least one product role under Apply mapping to.");
        if (model.MaxAssets is <= 0 or > 100)
            errors.Add("Maximum assets must be between 1 and 100.");
        if (!Enum.IsDefined(typeof(AkeneoAssetDestinationType), model.DestinationTypeId))
        {
            errors.Add("Select a valid asset destination.");
        }
        else
        {
            // Storage is a consequence of the destination in the current
            // implementation: pictures own a nopCommerce binary, while videos
            // and custom-property payloads retain external/source references.
            model.StorageModeId =
                (AkeneoAssetDestinationType)model.DestinationTypeId ==
                    AkeneoAssetDestinationType.ProductPicture
                    ? (int)AkeneoAssetStorageMode.ImportIntoNopCommerce
                    : (int)AkeneoAssetStorageMode.ExternalReference;
        }

        AkeneoAttributeDefinition source = null;
        if (!string.IsNullOrWhiteSpace(model.SourceAttributeCode))
        {
            try
            {
                source = await apiClient.GetAttributeByCodeAsync(model.SourceAttributeCode);
            }
            catch (Exception ex)
            {
                errors.Add($"The source attribute could not be validated: {ex.Message}");
            }
        }

        if (source != null)
        {
            if (source.Type is not ("pim_catalog_image" or
                    "pim_catalog_file" or
                    "pim_catalog_asset_collection"))
            {
                errors.Add(
                    $"Akeneo attribute '{source.Code}' has unsupported type '{source.Type}'. " +
                    "Choose an image, file, or asset collection attribute.");
                return errors;
            }

            var expectedSourceType = source.Type == "pim_catalog_asset_collection"
                ? AkeneoAssetSourceType.AssetCollection
                : AkeneoAssetSourceType.ProductMediaAttribute;
            model.SourceTypeId = (int)expectedSourceType;

            if (expectedSourceType == AkeneoAssetSourceType.AssetCollection)
            {
                model.AssetFamilyCode = source.ReferenceDataName;
                if (string.IsNullOrWhiteSpace(model.AssetFamilyCode))
                    errors.Add("The asset collection attribute does not identify an asset family.");
                if (string.IsNullOrWhiteSpace(model.AssetMediaAttributeCode))
                    errors.Add("Select the asset family media field.");
                else
                {
                    try
                    {
                        var fields = await apiClient.GetAssetFamilyAttributesAsync(model.AssetFamilyCode);
                        var field = fields.FirstOrDefault(candidate => string.Equals(
                            candidate.Code,
                            model.AssetMediaAttributeCode,
                            StringComparison.OrdinalIgnoreCase));
                        if (field == null || field.Type is not ("media_file" or "media_link"))
                        {
                            errors.Add("The selected asset field must be a media_file or media_link field.");
                        }
                        else
                        {
                            model.AssetMediaType = field.Type;
                            if ((AkeneoAssetDestinationType)model.DestinationTypeId == AkeneoAssetDestinationType.ProductVideo &&
                                field.Type != "media_link")
                            {
                                errors.Add("Product Video mappings require an Asset Manager media_link field.");
                            }
                        }

                        ValidateOptionalAssetField(
                            fields,
                            model.RoleAttributeCode,
                            "role",
                            errors);
                        ValidateOptionalAssetField(
                            fields,
                            model.SortOrderAttributeCode,
                            "sort-order",
                            errors);
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"The asset family fields could not be validated: {ex.Message}");
                    }
                }
            }
            else
            {
                model.AssetFamilyCode = null;
                model.AssetMediaAttributeCode = null;
                model.AssetMediaType = source.Type == "pim_catalog_image" ? "image" : "file";
                if ((AkeneoAssetDestinationType)model.DestinationTypeId == AkeneoAssetDestinationType.ProductVideo)
                    errors.Add("A product media attribute cannot directly create a nopCommerce video. Use an Asset Manager media_link field.");
            }
        }

        await ValidateFallbackSourceAsync(model, source, errors);

        if (!string.IsNullOrWhiteSpace(model.RoleValuesCsv) &&
            string.IsNullOrWhiteSpace(model.RoleAttributeCode))
        {
            errors.Add("Select a role field before entering allowed role codes.");
        }

        if ((AkeneoAssetDestinationType)model.DestinationTypeId == AkeneoAssetDestinationType.ProductPicture &&
            (AkeneoAssetStorageMode)model.StorageModeId != AkeneoAssetStorageMode.ImportIntoNopCommerce)
        {
            errors.Add("Product Picture mappings must import the binary into nopCommerce.");
        }

        if ((AkeneoAssetDestinationType)model.DestinationTypeId == AkeneoAssetDestinationType.CustomProperty &&
            string.IsNullOrWhiteSpace(model.CustomPropertyKey))
        {
            errors.Add("Custom property key is required for a Custom Property destination.");
        }

        ValidateTemplate(model.AltTextTemplate, "Alt text", errors);
        ValidateTemplate(model.TitleTextTemplate, "Title text", errors);
        ValidateTemplate(model.SeoFilenameTemplate, "SEO filename", errors);
        return errors;
    }

    private async Task ValidateFallbackSourceAsync(
        AkeneoAssetMappingModel model,
        AkeneoAttributeDefinition primarySource,
        ICollection<string> errors)
    {
        model.FallbackSourceAttributeCode =
            model.FallbackSourceAttributeCode.TrimOrNull();

        if (model.FallbackSourceAttributeCode == null)
            return;

        if (string.Equals(
                model.SourceAttributeCode?.Trim(),
                model.FallbackSourceAttributeCode,
                StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(
                "The fallback source must be different from the primary source attribute.");
            return;
        }

        AkeneoAttributeDefinition fallbackSource;
        try
        {
            fallbackSource = await apiClient.GetAttributeByCodeAsync(
                model.FallbackSourceAttributeCode);
        }
        catch (Exception ex)
        {
            errors.Add(
                $"The fallback source attribute could not be validated: {ex.Message}");
            return;
        }

        if (fallbackSource == null)
        {
            errors.Add("The fallback source attribute could not be found in Akeneo.");
            return;
        }

        if (fallbackSource.Type is not (
                "pim_catalog_image" or
                "pim_catalog_file" or
                "pim_catalog_asset_collection"))
        {
            errors.Add(
                $"Fallback attribute '{fallbackSource.Code}' has unsupported type " +
                $"'{fallbackSource.Type}'. Choose an image, file, or asset collection attribute.");
            return;
        }

        if (primarySource == null)
            return;

        var primarySourceType = primarySource.Type ==
            "pim_catalog_asset_collection"
                ? AkeneoAssetSourceType.AssetCollection
                : AkeneoAssetSourceType.ProductMediaAttribute;
        var fallbackSourceType = fallbackSource.Type ==
            "pim_catalog_asset_collection"
                ? AkeneoAssetSourceType.AssetCollection
                : AkeneoAssetSourceType.ProductMediaAttribute;

        if (primarySourceType != fallbackSourceType)
        {
            errors.Add(
                "The fallback source must use the same source type as the primary source " +
                "(product media or Asset Manager collection).");
            return;
        }

        if (primarySourceType == AkeneoAssetSourceType.AssetCollection &&
            !string.Equals(
                primarySource.ReferenceDataName,
                fallbackSource.ReferenceDataName,
                StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(
                "Asset collection fallback sources must reference the same Akeneo asset family " +
                "as the primary source.");
        }
    }

    private static void ValidateOptionalAssetField(
        IReadOnlyCollection<AkeneoAssetAttributeDefinition> fields,
        string fieldCode,
        string purpose,
        ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(fieldCode))
            return;

        if (!fields.Any(field => string.Equals(
                field.Code,
                fieldCode.Trim(),
                StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add($"The selected {purpose} field does not exist in the asset family.");
        }
    }

    private static bool IsMediaAttributeType(string type)
    {
        type = type?.Trim();

        return string.Equals(
                   type,
                   "media_file",
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   type,
                   "media_link",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveAssetAttributeLabel(
        AkeneoAssetAttributeDefinition attribute)
    {
        if (attribute == null)
            return string.Empty;

        // Replace with your existing localized-label resolver when available.
        return attribute.GetLabel()
               ?? attribute.Code
               ?? string.Empty;
    }


    private void ValidateTemplate(string template, string label, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(template))
            return;
        var validation = templateRenderer.Validate(template);
        foreach (var error in validation.Errors)
            errors.Add($"{label} template: {error}");
    }

    private static readonly JsonSerializerOptions WebJsonOptions =
        new(JsonSerializerDefaults.Web);

}
