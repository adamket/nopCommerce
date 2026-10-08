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
    IDateTimeHelper dateTimeHelper,
    IAkeneoSyncLeaseService syncLeaseService)
    : BaseAdminController
{
    // A live run renews its lease every 5 minutes; a heartbeat older than this
    // means the holder has almost certainly stopped.
    private static readonly TimeSpan StaleHeartbeatAge = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The catalog writer lock shared by every sync profile and catalog binding.
    /// </summary>
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    [HttpGet]
    public async Task<IActionResult> LockStatus()
    {
        var lease = await syncLeaseService.GetLeaseAsync(
            AkeneoConnectionConstants.CatalogWriterLockKey);

        return Json(await BuildLockStatusAsync(lease));
    }

    /// <summary>
    /// Releases the catalog writer lock regardless of its holder, and marks the
    /// run that held it as cancelled if it is still recorded as started. A run
    /// that is genuinely still running stops at its next lease renewal.
    /// </summary>
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    [HttpPost]
    public async Task<IActionResult> ReleaseLock()
    {
        var lease = await syncLeaseService.ForceReleaseAsync(
            AkeneoConnectionConstants.CatalogWriterLockKey);

        if (lease == null)
        {
            return Json(new
            {
                success = true,
                message = "No sync lock was held.",
                status = await BuildLockStatusAsync(null)
            });
        }

        if (lease.SyncRunRecordId > 0)
        {
            var runRecord = await syncRunRecordService
                .GetAkeneoSyncRunRecordByIdAsync(lease.SyncRunRecordId);

            if (runRecord != null &&
                runRecord.SyncStatusId == (int)SyncStatus.Started)
            {
                runRecord.SyncStatusId = (int)SyncStatus.Cancelled;
                runRecord.FinishedOnUtc ??= DateTime.UtcNow;
                runRecord.ErrorSummary =
                    "The sync lock was released manually; this run did not finish.";

                await syncRunRecordService.UpdateAkeneoSyncRunRecordAsync(runRecord);
            }
        }

        return Json(new
        {
            success = true,
            message = lease.SyncRunRecordId > 0
                ? $"Sync lock released. Run record {lease.SyncRunRecordId} was marked as cancelled."
                : "Sync lock released.",
            status = await BuildLockStatusAsync(null)
        });
    }

    private async Task<object> BuildLockStatusAsync(AkeneoSyncLease lease)
    {
        if (lease == null)
            return new { held = false };

        var now = DateTime.UtcNow;
        var runRecord = lease.SyncRunRecordId > 0
            ? await syncRunRecordService.GetAkeneoSyncRunRecordByIdAsync(lease.SyncRunRecordId)
            : null;

        async Task<string> ToStoreTimeAsync(DateTime utc) =>
            (await dateTimeHelper.ConvertToUserTimeAsync(
                DateTime.SpecifyKind(utc, DateTimeKind.Utc),
                DateTimeKind.Utc)).ToString("yyyy-MM-dd HH:mm");

        return new
        {
            held = true,
            expired = lease.ExpiresOnUtc <= now,
            stale = now - lease.HeartbeatOnUtc > StaleHeartbeatAge,
            heartbeatMinutesAgo = (int)Math.Max(0, (now - lease.HeartbeatOnUtc).TotalMinutes),
            runRecordId = lease.SyncRunRecordId > 0 ? lease.SyncRunRecordId : (int?)null,
            runStatus = runRecord != null && Enum.IsDefined(typeof(SyncStatus), runRecord.SyncStatusId)
                ? ((SyncStatus)runRecord.SyncStatusId).ToString()
                : null,
            runType = runRecord != null && Enum.IsDefined(typeof(SyncType), runRecord.SyncTypeId)
                ? ((SyncType)runRecord.SyncTypeId).ToString()
                : null,
            acquired = await ToStoreTimeAsync(lease.AcquiredOnUtc),
            lastHeartbeat = await ToStoreTimeAsync(lease.HeartbeatOnUtc),
            expires = await ToStoreTimeAsync(lease.ExpiresOnUtc)
        };
    }

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

    /// <summary>
    /// AJAX save for the create/edit page. Creates the profile when Id is 0.
    /// Returns field-keyed validation errors instead of re-rendering the view.
    /// </summary>
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    [HttpPost]
    public async Task<IActionResult> Save(
        AkeneoSyncProfileModel model)
    {
        var isNew = model.Id <= 0;
        AkeneoSyncProfile profile = null;

        if (!isNew)
        {
            profile = await syncProfileService.GetAkeneoSyncProfileByIdAsync(model.Id);

            if (profile == null)
            {
                return Json(new
                {
                    success = false,
                    message = "This sync profile no longer exists. It may have been deleted.",
                    errors = new Dictionary<string, string[]>()
                });
            }
        }

        ValidateAkeneoScope(model);

        if (!ModelState.IsValid)
        {
            return Json(new
            {
                success = false,
                message = "The profile was not saved. Fix the highlighted fields and try again.",
                errors = ModelState
                    .Where(entry => entry.Value?.Errors.Count > 0)
                    .ToDictionary(
                        entry => entry.Key,
                        entry => entry.Value.Errors
                            .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                                ? $"'{entry.Value.AttemptedValue}' is not a valid value."
                                : error.ErrorMessage)
                            .ToArray())
            });
        }

        profile ??= new AkeneoSyncProfile();

        ApplyModelToEntity(model, profile);

        if (isNew)
            await syncProfileService.InsertAkeneoSyncProfileAsync(profile);
        else
            await syncProfileService.UpdateAkeneoSyncProfileAsync(profile);

        return Json(new
        {
            success = true,
            created = isNew,
            id = profile.Id,
            enabled = profile.Enabled,
            message = isNew
                ? "Akeneo sync profile created."
                : "Akeneo sync profile saved.",
            urls = BuildProfileUrls(profile.Id)
        });
    }

    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    [HttpPost]
    public async Task<IActionResult> Delete(
        int id)
    {
        var profile = await syncProfileService.GetAkeneoSyncProfileByIdAsync(id);

        if (profile != null)
        {
            await syncProfileService.DeleteAkeneoSyncProfileAsync(profile);

            // Shown on the list page the client navigates to next.
            notificationService.SuccessNotification("Akeneo sync profile deleted successfully.");
        }

        return Json(new
        {
            success = true,
            redirectUrl = Url.Action(nameof(List))
        });
    }

    /// <summary>
    /// URLs the create/edit page needs once a profile has an id.
    /// </summary>
    private object BuildProfileUrls(int id) => new
    {
        edit = Url.Action(nameof(Edit), new { id }),
        delete = Url.Action(nameof(Delete), new { id }),
        run = Url.Action("ImportProductsByProfile", "AkeneoSync", new { id }),
        fullRun = Url.Action("ImportProductsByProfile", "AkeneoSync", new { id, fullSync = true })
    };




   


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