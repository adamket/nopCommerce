using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Filters;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
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
    public async Task<IActionResult> Create()
    {
        var model = await modelFactory.PrepareModelAsync(null);

        return View(EditViewPath, model);
    }

    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    [CheckAkeneoConnection]
    [HttpPost]
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
            Enabled = model.Enabled,
            VariantRelationshipModeId = model.VariantRelationshipModeId,
            PreserveExistingNopVariantStructure = model.PreserveExistingNopVariantStructure,
            AssociatedProductAttributeId = model.AssociatedProductAttributeId,
            AssociatedValueNameTemplate = model.AssociatedValueNameTemplate,
            HideChildProductsWhenRepresentedByParent = model.HideChildProductsWhenRepresentedByParent,
            DisplayOrder = model.DisplayOrder
        };

        await familyMappingService.InsertAsync(configuration);

        await SaveAxisMappingsAsync(configuration.Id, model.AxisMappings);

        notificationService.SuccessNotification("Family variant import configuration created.");

        return RedirectToAction(nameof(Edit), new { id = configuration.Id });
    }

    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    [HttpGet]
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
    [HttpPost]
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
        configuration.Enabled = model.Enabled;
        configuration.VariantRelationshipModeId = model.VariantRelationshipModeId;
        configuration.PreserveExistingNopVariantStructure = model.PreserveExistingNopVariantStructure;
        configuration.AssociatedProductAttributeId = model.AssociatedProductAttributeId;
        configuration.AssociatedValueNameTemplate = model.AssociatedValueNameTemplate;
        configuration.HideChildProductsWhenRepresentedByParent = model.HideChildProductsWhenRepresentedByParent;
        configuration.DisplayOrder = model.DisplayOrder;

        await familyMappingService.UpdateAsync(configuration);

        await SaveAxisMappingsAsync(configuration.Id, model.AxisMappings);

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

        var existing = await familyMappingService.GetByFamilyCodeAsync(model.AkeneoFamilyCode);
        if (existing != null && existing.Id != model.Id)
        {
            ModelState.AddModelError(
                nameof(model.AkeneoFamilyCode),
                "A configuration for this family already exists.");
        }


        var mode = (AkeneoVariantRelationshipMode)model.VariantRelationshipModeId;

        if (mode == AkeneoVariantRelationshipMode.AssociatedToProductAttributeValue &&
            !model.AssociatedProductAttributeId.HasValue)
        {
            ModelState.AddModelError(
                nameof(model.AssociatedProductAttributeId),
                "Associated-to-product mode requires a nopCommerce product attribute.");
        }

        if (mode == AkeneoVariantRelationshipMode.ProductAttributeCombinations &&
            !model.AxisMappings.Any(x =>
                !string.IsNullOrWhiteSpace(x.AkeneoAttributeCode) &&
                x.NopProductAttributeId > 0))
        {
            ModelState.AddModelError(
                nameof(model.AxisMappings),
                "Product attribute combination mode requires at least one axis mapping.");
        }
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
                DisplayOrder = model.DisplayOrder
            };

            await familyMappingService.InsertAxisMappingAsync(mapping);
        }
    }
}