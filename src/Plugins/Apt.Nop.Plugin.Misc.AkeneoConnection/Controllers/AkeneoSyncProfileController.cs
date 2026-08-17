using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Extensions;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Microsoft.AspNetCore.Mvc;
using Nop.Services.Helpers;
using Nop.Services.Messages;
using Nop.Services.Security;
using Nop.Web.Areas.Admin.Controllers;
using Nop.Web.Framework;
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
    IAkeneoProductBatchSyncService productImportService,
    IDateTimeHelper dateTimeHelper)
    : BaseAdminController
{
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    [HttpGet("admin/akeneo-connection/sync-profiles/list")]
    public async Task<IActionResult> List()
    {
        var model = await syncProfileModelFactory.PrepareListModelAsync();

        return View("~/Plugins/Apt.Misc.AkeneoConnection/Views/SyncProfile/List.cshtml", model);
    }

    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    [HttpGet("admin/akeneo-connection/sync-profiles/create")]
    public async Task<IActionResult> Create()
    {
        var model = await syncProfileModelFactory.PrepareModelAsync();

        return View("~/Plugins/Apt.Misc.AkeneoConnection/Views/SyncProfile/CreateOrUpdate.cshtml", model);
    }

    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    [HttpPost("admin/akeneo-connection/sync-profiles/create")]
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

    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    [HttpGet("admin/akeneo-connection/sync-profiles/edit/{id}")]
    public async Task<IActionResult> Edit(
        int id)
    {
        var profile = await syncProfileService.GetAkeneoSyncProfileByIdAsync(id);

        if (profile == null)
            return RedirectToAction(nameof(List));

        var model = await syncProfileModelFactory.PrepareModelAsync(profile);

        return View("~/Plugins/Apt.Misc.AkeneoConnection/Views/SyncProfile/CreateOrUpdate.cshtml", model);
    }

    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    [HttpPost("admin/akeneo-connection/sync-profiles/edit/{id}")]
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

    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
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




   


    private void ApplyModelToEntity(
        AkeneoSyncProfileModel model,
        AkeneoSyncProfile profile)
    {
        var now = DateTime.UtcNow;

        profile.Name = model.Name?.Trim();
        profile.Enabled = model.Enabled;
        profile.AkeneoChannel = model.AkeneoChannel?.Trim();

        profile.AkeneoLocales = model.SelectedAkeneoLocaleCodes.BuildCsv();

        profile.ProductWriteModeId = model.ProductWriteModeId;
        profile.UnmappedAttributeBehaviorId = model.UnmappedAttributeBehaviorId;

        profile.PageSize = model.PageSize <= 0 ? 100 : model.PageSize;
        profile.MaxProducts = model.MaxProducts;
        profile.ContinueOnError = model.ContinueOnError;
        profile.SaveRawPayloadSnapshot = model.SaveRawPayloadSnapshot;

        //profile.AddMappedCategories = model.AddMappedCategories;
        
        profile.CategorySyncModeId = model.CategorySyncModeId;
       // profile.AddMappedManufacturers = model.AddMappedManufacturers;
        profile.CreateMissingProductAttributeValues = model.CreateMissingProductAttributeValues;

        profile.AkeneoFamilyCodes =
            model.SelectedAkeneoFamilyCodes.BuildCsv();

        profile.AkeneoCategoryCodes =
            model.SelectedAkeneoCategoryCodes.BuildCsv();

        profile.CategoryFilterModeId = model.CategoryFilterModeId;
        profile.ProductEnabledFilterId = model.ProductEnabledFilterId;
        profile.CompletenessFilterId = model.CompletenessFilterId;
        profile.UpdatedAfterUtc = model.UpdatedAfter.HasValue
            ? dateTimeHelper.ConvertToUtcTime(
                DateTime.SpecifyKind(
                    model.UpdatedAfter.Value,
                    DateTimeKind.Unspecified),
                dateTimeHelper.DefaultStoreTimeZone)
            : null;
        profile.UpdatedSinceLastNDays = model.UpdatedSinceLastNDays;
        profile.ProductParentFilterModeId = model.ProductParentFilterModeId;
        profile.AdditionalSearchJson = model.AdditionalSearchJson?.Trim();

        profile.AkeneoProductGroupCodes = model.SelectedAkeneoProductGroupCodes
            ?.Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .BuildCsv();

        profile.CategoryFilterModeId = model.CategoryFilterModeId;


        profile.AkeneoLocales =
            model.SelectedAkeneoLocaleCodes.BuildCsv();

        profile.CurrencyCode =
            string.IsNullOrWhiteSpace(model.CurrencyCode)
                ? "USD"
                : model.CurrencyCode.Trim().ToUpperInvariant();

        profile.ProductFieldMissingValueBehaviorId =
            model.ProductFieldMissingValueBehaviorId;

        profile.SeoFieldMissingValueBehaviorId =
            model.SeoFieldMissingValueBehaviorId;

        profile.CustomPropertyMissingValueBehaviorId =
            model.CustomPropertyMissingValueBehaviorId;

        profile.CategorySyncModeId =
            model.CategorySyncModeId;

        profile.SpecificationAttributeSyncModeId =
            model.SpecificationAttributeSyncModeId;

        profile.ProductAttributeSyncModeId =
            model.ProductAttributeSyncModeId;

        profile.AssetSyncModeId =
            model.AssetSyncModeId;

        profile.MissingProductBehaviorId =
            model.MissingProductBehaviorId;

        profile.UpdatedFilterModeId =
            model.UpdatedFilterModeId;

        profile.IncludeLinkedAssetUpdates =
            model.IncludeLinkedAssetUpdates;

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

        if (!Enum.IsDefined(
                typeof(AkeneoCompletenessFilter),
                model.CompletenessFilterId))
        {
            ModelState.AddModelError(
                nameof(model.CompletenessFilterId),
                "Select a valid completeness rule.");
        }

        ValidateUpdatedFilter(model);
    }

    private void ValidateUpdatedFilter(
        AkeneoSyncProfileModel model)
    {
        if (!Enum.IsDefined(
                typeof(AkeneoUpdatedFilterMode),
                model.UpdatedFilterModeId))
        {
            ModelState.AddModelError(
                nameof(model.UpdatedFilterModeId),
                "Select a valid updated filter mode.");

            return;
        }

        var updatedFilterMode =
            (AkeneoUpdatedFilterMode)model.UpdatedFilterModeId;

        if (updatedFilterMode == AkeneoUpdatedFilterMode.FixedDate &&
            !model.UpdatedAfter.HasValue)
        {
            ModelState.AddModelError(
                nameof(model.UpdatedAfter),
                "Updated after is required when the fixed-date filter mode is selected.");
        }

        if (updatedFilterMode == AkeneoUpdatedFilterMode.FixedDate &&
            model.UpdatedAfter.HasValue)
        {
            var updatedAfter = DateTime.SpecifyKind(
                model.UpdatedAfter.Value,
                DateTimeKind.Unspecified);

            if (dateTimeHelper.DefaultStoreTimeZone.IsInvalidTime(updatedAfter))
            {
                ModelState.AddModelError(
                    nameof(model.UpdatedAfter),
                    $"The selected date and time does not exist in the store time zone ({dateTimeHelper.DefaultStoreTimeZone.Id}) because of a daylight-saving time transition.");
            }
            else if (dateTimeHelper.DefaultStoreTimeZone.IsAmbiguousTime(updatedAfter))
            {
                ModelState.AddModelError(
                    nameof(model.UpdatedAfter),
                    $"The selected date and time occurs twice in the store time zone ({dateTimeHelper.DefaultStoreTimeZone.Id}) because of a daylight-saving time transition. Select an unambiguous time.");
            }
        }

        if (updatedFilterMode == AkeneoUpdatedFilterMode.RollingDays &&
            (!model.UpdatedSinceLastNDays.HasValue ||
             model.UpdatedSinceLastNDays.Value <= 0))
        {
            ModelState.AddModelError(
                nameof(model.UpdatedSinceLastNDays),
                "Updated since last N days must be greater than zero when the rolling-days filter mode is selected.");
        }
    }
}