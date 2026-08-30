using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Filters;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Microsoft.AspNetCore.Mvc;
using Nop.Services.Messages;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Controllers;

[AuthorizeAdmin]
[Area(AreaNames.ADMIN)]
[AutoValidateAntiforgeryToken]
public class AkeneoFamilyMappingController(
    IAkeneoFamilyMappingService familyMappingService,
    IAkeneoFamilyMappingModelFactory modelFactory,
    IAkeneoApiClient akeneoApiClient,
    INotificationService notificationService)
    : BasePluginController
{
    private const string ListViewPath =
        $"{AkeneoConnectionConstants.PathToPlugin}/Views/FamilyMapping/List.cshtml";

    private const string EditViewPath =
        $"{AkeneoConnectionConstants.PathToPlugin}/Views/FamilyMapping/CreateOrUpdate.cshtml";

    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    [HttpGet("admin/akeneo-connection/family-mapping/list")]
    public async Task<IActionResult> List()
    {
        var model = await modelFactory.PrepareListModelAsync();
        return View(ListViewPath, model);
    }

    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    [HttpGet]
    public async Task<IActionResult> GetAttributeOptions(
        string attributeCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(attributeCode))
            return Json(Array.Empty<object>());

        attributeCode = attributeCode.Trim();

        var attribute = await akeneoApiClient.GetAttributeByCodeAsync(
            attributeCode,
            cancellationToken);

        if (attribute == null || !SupportsAttributeOptions(attribute.Type))
            return Json(Array.Empty<object>());

        var options =
            await akeneoApiClient.GetAttributeOptionDefinitionsAsync(
                attributeCode,
                cancellationToken: cancellationToken);

        return Json(options
            .OrderBy(option => option.SortOrder)
            .ThenBy(option => option.GetLabel())
            .Select(option => new
            {
                text = option.GetDisplayName(),
                value = option.Code
            }));
    }


    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    [HttpGet]
    public async Task<IActionResult> GetFamilyRuleFields(
        string familyCode,
        string familyVariantCode)
    {
        if (string.IsNullOrWhiteSpace(familyCode))
        {
            return Json(new
            {
                subModelAxes = Array.Empty<object>(),
                variantAttributes = Array.Empty<object>()
            });
        }

        familyCode = familyCode.Trim();

        var family = await akeneoApiClient.GetFamilyByCodeAsync(familyCode);
        var axes = string.IsNullOrWhiteSpace(familyVariantCode)
            ? await akeneoApiClient.GetFamilyVariantAxesAsync(familyCode)
            : await akeneoApiClient.GetFamilyVariantAxesForVariantAsync(
                familyCode,
                familyVariantCode);
        var attributes = await akeneoApiClient.GetAttributesAsync();

        var familyAttributeCodes = family?.Attributes?
                                       .ToHashSet(StringComparer.OrdinalIgnoreCase)
                                   ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var attributeByCode = attributes
            .Where(attribute => !string.IsNullOrWhiteSpace(attribute.Code))
            .GroupBy(
                attribute => attribute.Code,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);

        var optionBackedAttributes = attributes
            .Where(attribute =>
                familyAttributeCodes.Contains(attribute.Code) &&
                SupportsAttributeOptions(attribute.Type))
            .OrderBy(attribute => attribute.GetLabel())
            .Select(attribute => new
            {
                text = attribute.GetDisplayName(),
                value = attribute.Code
            });

        return Json(new
        {
            subModelAxes = axes
                .Where(axis => axis.Level == 1)
                .Select(axis => new
                {
                    text = $"{axis.AttributeCode} (level {axis.Level})",
                    value = axis.AttributeCode,
                    supportsOptions =
                        attributeByCode.TryGetValue(
                            axis.AttributeCode,
                            out var attribute) &&
                        SupportsAttributeOptions(attribute.Type)
                }),

            variantAttributes = optionBackedAttributes
        });
    }

    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    [HttpGet]
    public async Task<IActionResult> GetFamilyVariants(string familyCode)
    {
        var result = new List<object>
        {
            new { text = "All family variants (default)", value = string.Empty }
        };

        if (string.IsNullOrWhiteSpace(familyCode))
            return Json(result);

        var variants = await akeneoApiClient.GetFamilyVariantsAsync(
            familyCode.Trim());

        result.AddRange(variants
            .Where(variant => !string.IsNullOrWhiteSpace(variant.Code))
            .OrderBy(variant => variant.GetLabel())
            .ThenBy(variant => variant.Code)
            .Select(variant => (object)new
            {
                text = variant.GetDisplayName(),
                value = variant.Code
            }));

        return Json(result);
    }

    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    [HttpGet("admin/akeneo-connection/family-mapping/create")]
    public async Task<IActionResult> Create()
    {
        var model = await modelFactory.PrepareModelAsync(null);

        return View(EditViewPath, model);
    }

    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    [CheckAkeneoConnection]
    [HttpPost("admin/akeneo-connection/family-mapping/create")]
    public async Task<IActionResult> Create(AkeneoFamilyMappingModel model, bool connectionValid)
    {
        if (!connectionValid)
        {
            notificationService.ErrorNotification("Unable to connect to the Akeneo instance. Please check your configuration.");
            return View(EditViewPath, model);
        }

        await ValidateModelAsync(model);
        if (!ModelState.IsValid)
        {
            model = await modelFactory.PrepareModelAsync(model);
            return View(EditViewPath, model);
        }

        var configuration = new AkeneoFamilyMapping
        {
            AkeneoFamilyCode = model.AkeneoFamilyCode?.Trim(),
            AkeneoFamilyVariantCode = NormalizeVariantCode(
                model.AkeneoFamilyVariantCode),
            Enabled = model.Enabled,
            VariantRelationshipModeId = model.VariantRelationshipModeId,
            ProductModelHierarchyModeId = model.ProductModelHierarchyModeId,
            PreserveExistingNopVariantStructure = model.PreserveExistingNopVariantStructure,
            AssociatedProductAttributeId = model.AssociatedProductAttributeId,
            AssociatedValueNameTemplate = model.AssociatedValueNameTemplate,
            HideChildProductsWhenRepresentedByParent = model.HideChildProductsWhenRepresentedByParent,
            DisplayOrder = model.DisplayOrder
        };

        await familyMappingService.InsertAsync(configuration);

        await SaveAxisMappingsAsync(configuration.Id, model.AxisMappings);
        await SaveSubModelRulesAsync(configuration.Id, model.SubModelRules);

        notificationService.SuccessNotification("Family variant import configuration created.");

        return RedirectToAction(nameof(Edit), new { id = configuration.Id });
    }

    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    [HttpGet("admin/akeneo-connection/family-mapping/edit/{id}")]
    [CheckAkeneoConnection]
    public async Task<IActionResult> Edit(int id, bool connectionValid)
    {
        if (!connectionValid)
        {
            notificationService.ErrorNotification("Unable to connect to the Akeneo instance. Please check your configuration.");
            return View(EditViewPath, new AkeneoFamilyMappingModel());
        }

        var configuration = await familyMappingService.GetByIdAsync(id);

        if (configuration == null)
            return RedirectToAction(nameof(List));

        var model = await modelFactory.PrepareModelAsync(null, configuration);

        return View(EditViewPath, model);
    }

    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    [HttpPost("admin/akeneo-connection/family-mapping/edit/{id}")]
    [CheckAkeneoConnection]
    public async Task<IActionResult> Edit(AkeneoFamilyMappingModel model, bool connectionValid)
    {
        if (!connectionValid)
        {
            notificationService.ErrorNotification("Unable to connect to the Akeneo instance. Please check your configuration.");
            return View(EditViewPath, model);
        }

        var configuration = await familyMappingService.GetByIdAsync(model.Id);
        if (configuration == null)
            return RedirectToAction(nameof(List));

        await ValidateModelAsync(model);

        if (!ModelState.IsValid)
        {
            model = await modelFactory.PrepareModelAsync(model, configuration);
            return View(EditViewPath, model);
        }

        configuration.AkeneoFamilyCode = model.AkeneoFamilyCode?.Trim();
        configuration.AkeneoFamilyVariantCode = NormalizeVariantCode(
            model.AkeneoFamilyVariantCode);
        configuration.Enabled = model.Enabled;
        configuration.VariantRelationshipModeId = model.VariantRelationshipModeId;
        configuration.ProductModelHierarchyModeId = model.ProductModelHierarchyModeId;
        configuration.PreserveExistingNopVariantStructure = model.PreserveExistingNopVariantStructure;
        configuration.AssociatedProductAttributeId = model.AssociatedProductAttributeId;
        configuration.AssociatedValueNameTemplate = model.AssociatedValueNameTemplate;
        configuration.HideChildProductsWhenRepresentedByParent = model.HideChildProductsWhenRepresentedByParent;
        configuration.DisplayOrder = model.DisplayOrder;

        await familyMappingService.UpdateAsync(configuration);

        await SaveAxisMappingsAsync(configuration.Id, model.AxisMappings);
        await SaveSubModelRulesAsync(configuration.Id, model.SubModelRules);

        notificationService.SuccessNotification("Family variant import configuration updated.");

        return RedirectToAction(nameof(Edit), new { id = configuration.Id });
    }

    //   [CheckAkeneoConnection]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    [HttpPost]
    public async Task<IActionResult> Delete(int id, bool connectionValid)
    {
        var configuration = await familyMappingService.GetByIdAsync(id);

        if (configuration == null)
            return RedirectToAction(nameof(List));


        //if (!connectionValid)
        //{
        //    notificationService.ErrorNotification("Unable to connect to the Akeneo instance. Please check your configuration.");
        //    return View(EditViewPath, model);
        //}


        var axisMappings =
            await familyMappingService.GetAxisMappingsAsync(configuration.Id);

        foreach (var mapping in axisMappings)
            await familyMappingService.DeleteAxisMappingAsync(mapping);

        var subModelRules =
            await familyMappingService.GetSubModelRulesAsync(configuration.Id);

        foreach (var rule in subModelRules)
            await familyMappingService.DeleteSubModelRuleAsync(rule);

        await familyMappingService.DeleteAsync(configuration);

        notificationService.SuccessNotification(
            "Family variant import configuration deleted.");

        return RedirectToAction(nameof(List));
    }

    private async Task ValidateModelAsync(AkeneoFamilyMappingModel model)
    {
        if (string.IsNullOrWhiteSpace(model.AkeneoFamilyCode))
        {
            ModelState.AddModelError(
                nameof(model.AkeneoFamilyCode),
                "Akeneo family is required.");
            return;
        }

        var existing = await familyMappingService.GetByFamilyAndVariantCodeAsync(
            model.AkeneoFamilyCode,
            model.AkeneoFamilyVariantCode);
        if (existing != null && existing.Id != model.Id)
        {
            ModelState.AddModelError(
                nameof(model.AkeneoFamilyVariantCode),
                string.IsNullOrWhiteSpace(model.AkeneoFamilyVariantCode)
                    ? "A family-wide default configuration already exists for this family."
                    : "A configuration for this family and family variant already exists.");
        }


        var mode = (AkeneoVariantRelationshipMode)model.VariantRelationshipModeId;

        if (!Enum.IsDefined(
                typeof(AkeneoProductModelHierarchyMode),
                model.ProductModelHierarchyModeId))
        {
            ModelState.AddModelError(
                nameof(model.ProductModelHierarchyModeId),
                "The selected product-model hierarchy mode is invalid.");
        }

        if (mode == AkeneoVariantRelationshipMode.AssociatedToProductAttributeValue &&
            !AkeneoAssociatedValueNameTemplate.TryValidate(
                model.AssociatedValueNameTemplate,
                out var templateError))
        {
            ModelState.AddModelError(
                nameof(model.AssociatedValueNameTemplate),
                templateError);
        }

        model.AxisMappings ??= new List<AkeneoFamilyVariantAxisMappingModel>();
        model.SubModelRules ??= new List<AkeneoFamilySubModelRuleModel>();

        if (mode == AkeneoVariantRelationshipMode.ProductAttributeCombinations &&
            !model.AxisMappings.Any(x =>
                !string.IsNullOrWhiteSpace(x.AkeneoAttributeCode) &&
                x.NopProductAttributeId > 0))
        {
            ModelState.AddModelError(
                nameof(model.AxisMappings),
                "Product attribute combination mode requires at least one axis mapping.");
        }

        ValidateSubModelRules(model.SubModelRules);
    }

    private void ValidateSubModelRules(IList<AkeneoFamilySubModelRuleModel> rules)
    {
        for (var index = 0; index < rules.Count; index++)
        {
            var rule = rules[index];
            var hasSubModelCode = !string.IsNullOrWhiteSpace(rule.AkeneoAxisAttributeCode);
            var hasSubModelValue = !string.IsNullOrWhiteSpace(rule.TriggerValue);
            var hasVariantCode = !string.IsNullOrWhiteSpace(rule.VariantAxisAttributeCode);
            var hasVariantValue = !string.IsNullOrWhiteSpace(rule.VariantTriggerValue);

            if (!hasSubModelCode && !hasSubModelValue && !hasVariantCode && !hasVariantValue)
            {
                ModelState.AddModelError(
                    $"SubModelRules[{index}]",
                    $"Sub-model rule {index + 1} must define at least one condition.");
                continue;
            }

            if (hasSubModelCode != hasSubModelValue)
            {
                ModelState.AddModelError(
                    $"SubModelRules[{index}].TriggerValue",
                    $"Sub-model rule {index + 1} requires both a sub-model axis and trigger value.");
            }

            if (hasVariantCode != hasVariantValue)
            {
                ModelState.AddModelError(
                    $"SubModelRules[{index}].VariantTriggerValue",
                    $"Sub-model rule {index + 1} requires both a variant axis and trigger value.");
            }

            if (!Enum.IsDefined(typeof(AkeneoVariantRelationshipMode), rule.VariantRelationshipOverrideModeId))
            {
                ModelState.AddModelError(
                    $"SubModelRules[{index}].VariantRelationshipOverrideModeId",
                    $"Sub-model rule {index + 1} has an invalid relationship override mode.");
            }
        }
    }

    private static bool SupportsAttributeOptions(string attributeType)
    {
        return string.Equals(
                   attributeType,
                   "pim_catalog_simpleselect",
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   attributeType,
                   "pim_catalog_multiselect",
                   StringComparison.OrdinalIgnoreCase);
    }

    private async Task SaveAxisMappingsAsync(
        int configurationId,
        IList<AkeneoFamilyVariantAxisMappingModel> models)
    {
        var existing = await familyMappingService.GetAxisMappingsAsync(configurationId);

        foreach (var existingMapping in existing)
            await familyMappingService.DeleteAxisMappingAsync(existingMapping);

        foreach (var model in models.Where(x =>
                     !string.IsNullOrWhiteSpace(x.AkeneoAttributeCode) &&
                     x.NopProductAttributeId > 0))
        {
            var mapping = new AkeneoFamilyVariantAxisMapping
            {
                FamilyVariantImportConfigurationId = configurationId,
                AkeneoAttributeCode = model.AkeneoAttributeCode.Trim(),
                NopProductAttributeId = model.NopProductAttributeId,
                IsRequired = model.IsRequired,
                DisplayOrder = model.DisplayOrder,
                AkeneoVariantAxisLevel = model.AkeneoVariantAxisLevel > 0
                    ? model.AkeneoVariantAxisLevel
                    : 1
            };

            await familyMappingService.InsertAxisMappingAsync(mapping);
        }
    }

    private async Task SaveSubModelRulesAsync(
        int familyMappingId,
        IList<AkeneoFamilySubModelRuleModel> models)
    {
        var existing = await familyMappingService.GetSubModelRulesAsync(familyMappingId);

        foreach (var existingRule in existing)
            await familyMappingService.DeleteSubModelRuleAsync(existingRule);

        models ??= new List<AkeneoFamilySubModelRuleModel>();

        foreach (var item in models
                     .Select((model, index) => new { Model = model, Index = index })
                     .Where(x =>
                         !string.IsNullOrWhiteSpace(x.Model.AkeneoAxisAttributeCode) ||
                         !string.IsNullOrWhiteSpace(x.Model.VariantAxisAttributeCode)))
        {
            var model = item.Model;

            var rule = new AkeneoFamilySubModelRule
            {
                FamilyMappingId = familyMappingId,
                AkeneoAxisAttributeCode = model.AkeneoAxisAttributeCode?.Trim(),
                TriggerValue = model.TriggerValue?.Trim(),
                VariantAxisAttributeCode = model.VariantAxisAttributeCode?.Trim(),
                VariantTriggerValue = model.VariantTriggerValue?.Trim(),
                VariantRelationshipOverrideModeId = model.VariantRelationshipOverrideModeId,
                MergeAncestorValues = model.MergeAncestorValues,
                DisplayOrder = model.DisplayOrder > 0
                    ? model.DisplayOrder
                    : item.Index + 1
            };

            await familyMappingService.InsertSubModelRuleAsync(rule);
        }
    }

    private static string NormalizeVariantCode(string value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
}
