using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Extensions;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models.SyncProfiles;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Humanizer;
using Microsoft.AspNetCore.Mvc;
using Nop.Services.Messages;
using Nop.Web.Areas.Admin.Controllers;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Controllers;

[AuthorizeAdmin]
[Area(AreaNames.ADMIN)]
[AutoValidateAntiforgeryToken]
public class AkeneoSyncProfileController(
    IAkeneoSyncProfileService syncProfileService,
    IAkeneoSyncProfileModelFactory syncProfileModelFactory,
    INotificationService notificationService,
    IAkeneoSyncRunRecordService syncRunRecordService,
    IAkeneoProductImportService productImportService)
    : BaseAdminController
{
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var model = await syncProfileModelFactory.PrepareListModelAsync();

        return View("~/Plugins/Apt.Misc.AkeneoConnection/Views/SyncProfile/List.cshtml", model);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var model = await syncProfileModelFactory.PrepareModelAsync();

        return View("~/Plugins/Apt.Misc.AkeneoConnection/Views/SyncProfile/CreateOrUpdate.cshtml", model);
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        AkeneoSyncProfileModel model)
    {
        ValidateAkeneoScope(model);

        if (!ModelState.IsValid)
        {
            await syncProfileModelFactory.PrepareAvailableOptionsAsync(model);

            return View("~/Plugins/Apt.Misc.AkeneoConnection/Views/SyncProfile/CreateOrUpdate.cshtml", model);
        }

        var profile = new AkeneoSyncProfile();

        ApplyModelToEntity(model, profile);

        await syncProfileService.InsertAkeneoSyncProfileAsync(profile);

        notificationService.SuccessNotification("Akeneo sync profile created successfully.");

        return RedirectToAction(nameof(Edit), new { id = profile.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(
        int id)
    {
        var profile = await syncProfileService.GetAkeneoSyncProfileByIdAsync(id);

        if (profile == null)
            return RedirectToAction(nameof(List));

        var model = await syncProfileModelFactory.PrepareModelAsync(profile);

        return View("~/Plugins/Apt.Misc.AkeneoConnection/Views/SyncProfile/CreateOrUpdate.cshtml", model);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(
        AkeneoSyncProfileModel model)
    {
        var profile = await syncProfileService.GetAkeneoSyncProfileByIdAsync(model.Id);

        if (profile == null)
            return RedirectToAction(nameof(List));

        ValidateAkeneoScope(model);

        if (!ModelState.IsValid)
        {
            await syncProfileModelFactory.PrepareAvailableOptionsAsync(model);

            return View("~/Plugins/Apt.Misc.AkeneoConnection/Views/SyncProfile/CreateOrUpdate.cshtml", model);
        }

        ApplyModelToEntity(model, profile);

        await syncProfileService.UpdateAkeneoSyncProfileAsync(profile);

        notificationService.SuccessNotification("Akeneo sync profile updated successfully.");

        return RedirectToAction(nameof(Edit), new { id = profile.Id });
    }

    [HttpPost]
    public async Task<IActionResult> Delete(
        int id)
    {
        var profile = await syncProfileService.GetAkeneoSyncProfileByIdAsync(id);

        if (profile == null)
            return RedirectToAction(nameof(List));

        await syncProfileService.DeleteAkeneoSyncProfileAsync(profile);

        notificationService.SuccessNotification("Akeneo sync profile deleted successfully.");

        return RedirectToAction(nameof(List));
    }




   


    private static void ApplyModelToEntity(
        AkeneoSyncProfileModel model,
        AkeneoSyncProfile profile)
    {
        var now = DateTime.UtcNow;

        profile.Name = model.Name?.Trim();
        profile.Enabled = model.Enabled;
        profile.AkeneoChannel = model.AkeneoChannel?.Trim();

        profile.AkeneoLocales = model.SelectedAkeneoLocaleCodes.BuildCsv();

        profile.RootCategoryCode = model.RootCategoryCode?.Trim();
        profile.ImportModeId = model.ImportModeId;
        profile.UnmappedAttributeBehaviorId = model.UnmappedAttributeBehaviorId;

        profile.PageSize = model.PageSize <= 0 ? 100 : model.PageSize;
        profile.MaxProducts = model.MaxProducts;
        profile.ContinueOnError = model.ContinueOnError;
        profile.SaveRawPayloadSnapshot = model.SaveRawPayloadSnapshot;

        profile.AddMappedCategories = model.AddMappedCategories;
        profile.AddMappedManufacturers = model.AddMappedManufacturers;
        profile.CreateMissingSpecificationAttributeOptions = model.CreateMissingSpecificationAttributeOptions;
        profile.CreateMissingProductAttributeValues = model.CreateMissingProductAttributeValues;

        profile.AkeneoFamilyCodes = model.AkeneoFamilyCodes?.Trim();
        profile.AkeneoCategoryCodes = model.AkeneoCategoryCodes?.Trim();
        profile.CategoryFilterModeId = model.CategoryFilterModeId;
        profile.ProductEnabledFilterId = model.ProductEnabledFilterId;
        profile.UpdatedAfterUtc = model.UpdatedAfterUtc;
        profile.UpdatedSinceLastNDays = model.UpdatedSinceLastNDays;
        profile.ProductParentFilterModeId = model.ProductParentFilterModeId;
        profile.AdditionalSearchJson = model.AdditionalSearchJson?.Trim();

        profile.AkeneoProductGroupCodes = model.SelectedAkeneoProductGroupCodes
            ?.Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .BuildCsv();

        profile.CategoryFilterModeId = model.CategoryFilterModeId;

        if (profile.Id <= 0)
            profile.CreatedOnUtc = now;

        profile.UpdatedOnUtc = now;
    }

    private void ValidateAkeneoScope(
        AkeneoSyncProfileModel model)
    {
        if (string.IsNullOrWhiteSpace(model.AkeneoChannel))
            ModelState.AddModelError(nameof(model.AkeneoChannel), "Akeneo channel is required.");

        if (model.SelectedAkeneoLocaleCodes == null ||
            !model.SelectedAkeneoLocaleCodes.Any(locale => !string.IsNullOrWhiteSpace(locale)))
        {
            ModelState.AddModelError(nameof(model.SelectedAkeneoLocaleCodes), "At least one Akeneo locale is required.");
        }
    }






}