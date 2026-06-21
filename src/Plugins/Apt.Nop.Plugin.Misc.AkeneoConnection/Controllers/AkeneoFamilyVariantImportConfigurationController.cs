using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;
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
public class AkeneoFamilyVariantImportConfigurationController : BasePluginController
{
    private const string ListViewPath =
        "~/Plugins/Apt.Misc.AkeneoConnection/Views/FamilyVariantImportConfiguration/List.cshtml";

    private const string EditViewPath =
        "~/Plugins/Apt.Misc.AkeneoConnection/Views/FamilyVariantImportConfiguration/Edit.cshtml";

    private readonly IAkeneoFamilyVariantImportConfigurationService _configurationService;
    private readonly IAkeneoFamilyVariantImportConfigurationModelFactory _modelFactory;
    private readonly INotificationService _notificationService;

    public AkeneoFamilyVariantImportConfigurationController(
        IAkeneoFamilyVariantImportConfigurationService configurationService,
        IAkeneoFamilyVariantImportConfigurationModelFactory modelFactory,
        INotificationService notificationService)
    {
        _configurationService = configurationService;
        _modelFactory = modelFactory;
        _notificationService = notificationService;
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var model = await _modelFactory.PrepareListModelAsync();
        return View(ListViewPath, model);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var model = await _modelFactory.PrepareModelAsync(null);
        return View(EditViewPath, model);
    }

    [HttpPost]
    public async Task<IActionResult> Create(AkeneoFamilyVariantImportConfigurationModel model)
    {
        ValidateModel(model);

        if (!ModelState.IsValid)
        {
            model = await _modelFactory.PrepareModelAsync(model);
            return View(EditViewPath, model);
        }

        var configuration = new AkeneoFamilyVariantImportConfiguration
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

        await _configurationService.InsertAsync(configuration);

        await SaveAxisMappingsAsync(configuration.Id, model.AxisMappings);

        _notificationService.SuccessNotification("Family variant import configuration created.");

        return RedirectToAction(nameof(Edit), new { id = configuration.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var configuration = await _configurationService.GetByIdAsync(id);

        if (configuration == null)
            return RedirectToAction(nameof(List));

        var model = await _modelFactory.PrepareModelAsync(null, configuration);

        return View(EditViewPath, model);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(AkeneoFamilyVariantImportConfigurationModel model)
    {
        var configuration = await _configurationService.GetByIdAsync(model.Id);

        if (configuration == null)
            return RedirectToAction(nameof(List));

        ValidateModel(model);

        if (!ModelState.IsValid)
        {
            model = await _modelFactory.PrepareModelAsync(model, configuration);
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

        await _configurationService.UpdateAsync(configuration);

        await SaveAxisMappingsAsync(configuration.Id, model.AxisMappings);

        _notificationService.SuccessNotification("Family variant import configuration updated.");

        return RedirectToAction(nameof(Edit), new { id = configuration.Id });
    }

    private void ValidateModel(AkeneoFamilyVariantImportConfigurationModel model)
    {
        if (string.IsNullOrWhiteSpace(model.AkeneoFamilyCode))
        {
            ModelState.AddModelError(
                nameof(model.AkeneoFamilyCode),
                "Akeneo family is required.");
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
        var existing = await _configurationService.GetAxisMappingsAsync(configurationId);

        foreach (var existingMapping in existing)
            await _configurationService.DeleteAxisMappingAsync(existingMapping);

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

            await _configurationService.InsertAxisMappingAsync(mapping);
        }
    }
}