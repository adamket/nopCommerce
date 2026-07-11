using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Filters;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Microsoft.AspNetCore.Mvc;
using Nop.Services.Messages;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Controllers;

[AuthorizeAdmin]
[Area(AreaNames.ADMIN)]
[AutoValidateAntiforgeryToken]
public class AkeneoFamilyMappingController(
    IAkeneoFamilyMappingService configurationService,
    IAkeneoFamilyMappingModelFactory modelFactory,
    INotificationService notificationService)
    : BasePluginController
{
    private const string ListViewPath =
        $"{AkeneoConnectionConstants.PathToPlugin}/Views/FamilyMapping/List.cshtml";

    private const string EditViewPath =
        $"{AkeneoConnectionConstants.PathToPlugin}/Views/FamilyMapping/CreateOrUpdate.cshtml";

    [HttpGet("admin/akeneo-connection/family-mapping/list")]
    public async Task<IActionResult> List()
    {
        var model = await modelFactory.PrepareListModelAsync();
        return View(ListViewPath, model);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var model = await modelFactory.PrepareModelAsync(null);

        return View(EditViewPath, model);
    }

    [CheckAkeneoConnection]
    [HttpPost]
    public async Task<IActionResult> Create(AkeneoFamilyMappingModel model, bool connectionValid)
    {
        await ValidateModelAsync(model);

        if (!ModelState.IsValid)
        {
            model = await modelFactory.PrepareModelAsync(model);
            return View(EditViewPath, model);
        }

        if (!connectionValid)
        {
            notificationService.ErrorNotification("Unable to connect to the Akeneo instance. Please check your configuration.");
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

        await configurationService.InsertAsync(configuration);

        await SaveAxisMappingsAsync(configuration.Id, model.AxisMappings);

        notificationService.SuccessNotification("Family variant import configuration created.");

        return RedirectToAction(nameof(Edit), new { id = configuration.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var configuration = await configurationService.GetByIdAsync(id);

        if (configuration == null)
            return RedirectToAction(nameof(List));

        var model = await modelFactory.PrepareModelAsync(null, configuration);

        return View(EditViewPath, model);
    }

    [HttpPost]
    [CheckAkeneoConnection]
    public async Task<IActionResult> Edit(AkeneoFamilyMappingModel model, bool connectionValid)
    {
        var configuration = await configurationService.GetByIdAsync(model.Id);

        if (configuration == null)
            return RedirectToAction(nameof(List));

        await ValidateModelAsync(model);

        if (!ModelState.IsValid)
        {
            model = await modelFactory.PrepareModelAsync(model, configuration);
            return View(EditViewPath, model);
        }

        if (!connectionValid)
        {
            notificationService.ErrorNotification("Unable to connect to the Akeneo instance. Please check your configuration.");
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

        await configurationService.UpdateAsync(configuration);

        await SaveAxisMappingsAsync(configuration.Id, model.AxisMappings);

        notificationService.SuccessNotification("Family variant import configuration updated.");

        return RedirectToAction(nameof(Edit), new { id = configuration.Id });
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

        var existing = await configurationService.GetByFamilyCodeAsync(model.AkeneoFamilyCode);
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
        var existing = await configurationService.GetAxisMappingsAsync(configurationId);

        foreach (var existingMapping in existing)
            await configurationService.DeleteAxisMappingAsync(existingMapping);

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

            await configurationService.InsertAxisMappingAsync(mapping);
        }
    }
}