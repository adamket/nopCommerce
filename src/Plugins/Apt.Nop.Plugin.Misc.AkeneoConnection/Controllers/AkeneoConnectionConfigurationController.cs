using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Core;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Controllers;

[AuthorizeAdmin]
[Area(AreaNames.ADMIN)]
[AutoValidateAntiforgeryToken]
public class AkeneoConnectionConfigurationController(
    IAkeneoApiClient akeneoApiClient,
    IAkeneoConfigurationExportService configurationExportService,
    IAkeneoConfigurationImportService configurationImportService,
    ILanguageService languageService,
    ILocalizationService localizationService,
    INotificationService notificationService,
    IPermissionService permissionService,
    ISettingService settingService,
    IStoreContext storeContext,
    IAkeneoSyncProfileService syncProfileService)
    : BasePluginController
{

    private async Task PrepareSyncContextOptionsAsync(AkeneoConfigurationModel model)
    {
        var syncProfiles = await syncProfileService.GetAllAkeneoSyncProfilesAsync();
        model.AvailableSyncProfiles = syncProfiles.Select(q=> new SelectListItem(q.Name, q.Id.ToString())).ToList();
        model.AvailableSyncProfiles.Insert(0, new SelectListItem("None selected", ""));

    }

    #region Methods
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    [HttpPost("admin/akeneo-connection/test-connection")]
    public async Task<IActionResult> TestConnection(AkeneoApiCredentials apiCredentials, CancellationToken cancellationToken)
    {

        var result = await akeneoApiClient.TestConnectionAsync(
            apiCredentials, cancellationToken);

        return Json(new
        {
            success = result.Success,
            message = result.Message
        });
    }


    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    [HttpGet("admin/akeneo-connection/export-configuration")]
    public async Task<IActionResult> ExportConfiguration(
        CancellationToken cancellationToken)
    {
        var storeScope =
            await storeContext.GetActiveStoreScopeConfigurationAsync();

        var exportBytes = await configurationExportService.ExportAsync(
            storeScope,
            cancellationToken);

        var fileName =
            $"akeneo-connection-configuration-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json";

        Response.Headers["Cache-Control"] = "no-store, no-cache";
        Response.Headers["Pragma"] = "no-cache";

        return File(exportBytes, "application/json", fileName);
    }


    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    [HttpPost("admin/akeneo-connection/import-configuration")]
    [RequestSizeLimit(11 * 1024 * 1024)]
    public async Task<IActionResult> ImportConfiguration(
        IFormFile configurationFile,
        bool confirmReplace,
        CancellationToken cancellationToken)
    {
        if (!confirmReplace)
        {
            notificationService.ErrorNotification("Confirm replacement of all mapping configuration before importing.");
            return RedirectToAction(nameof(Configure));
        }
        if (configurationFile == null || configurationFile.Length == 0 || configurationFile.Length > 10 * 1024 * 1024)
        {
            notificationService.ErrorNotification("Choose a non-empty configuration JSON file no larger than 10 MB.");
            return RedirectToAction(nameof(Configure));
        }
        using var reader = new StreamReader(configurationFile.OpenReadStream());
        var json = await reader.ReadToEndAsync(cancellationToken);
        try
        {
            await configurationImportService.ImportAsync(json, cancellationToken);
            notificationService.SuccessNotification("Mapping configuration replaced successfully. Connection settings and sync profiles were preserved.");
        }
        catch (ArgumentException exception)
        {
            notificationService.ErrorNotification(exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            notificationService.ErrorNotification(exception.Message);
        }
        return RedirectToAction(nameof(Configure));
    }

    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    [HttpGet("admin/akeneo-connection/configure")]
    public async Task<IActionResult> Configure()
    {
        //load settings for a chosen store scope
        var storeScope = await storeContext.GetActiveStoreScopeConfigurationAsync();
        var akeneoConnectionSettings = await settingService.LoadSettingAsync<AkeneoConnectionSettings>(storeScope);

        var model = new AkeneoConfigurationModel
        {
            ActiveStoreScopeConfiguration = storeScope,
            AkeneoConnectionBaseUrl = akeneoConnectionSettings.AkeneoConnectionBaseUrl,
            AkeneoConnectionClientId = akeneoConnectionSettings.AkeneoConnectionClientId,
            AkeneoConnectionClientSecret = akeneoConnectionSettings.AkeneoConnectionClientSecret,
            AkeneoConnectionUsername = akeneoConnectionSettings.AkeneoConnectionUsername,
            AkeneoConnectionPassword = akeneoConnectionSettings.AkeneoConnectionPassword,
            DefaultSyncProfileId = akeneoConnectionSettings.DefaultSyncProfileId
        };

        var alertSettings = await settingService.LoadSettingAsync<AkeneoConnectionSettings>();
        model.SyncAlertsEnabled = alertSettings.SyncAlertsEnabled;
        model.SyncAlertEmails = alertSettings.SyncAlertEmails;
        model.SyncAlertMinimumFailedProducts = alertSettings.SyncAlertMinimumFailedProducts;
        model.SyncAlertStaleAfterHours = alertSettings.SyncAlertStaleAfterHours;

        if (storeScope > 0)
        {
            model.AkeneoConnectionBaseUrl_OverrideForStore = await settingService.SettingExistsAsync(akeneoConnectionSettings, x => x.AkeneoConnectionBaseUrl, storeScope);
            model.AkeneoConnectionClientId_OverrideForStore = await settingService.SettingExistsAsync(akeneoConnectionSettings, x => x.AkeneoConnectionClientId, storeScope);
            model.AkeneoConnectionClientSecret_OverrideForStore = await settingService.SettingExistsAsync(akeneoConnectionSettings, x => x.AkeneoConnectionClientSecret, storeScope);
            model.AkeneoConnectionUsername_OverrideForStore = await settingService.SettingExistsAsync(akeneoConnectionSettings, x => x.AkeneoConnectionUsername, storeScope);
            model.AkeneoConnectionPassword_OverrideForStore = await settingService.SettingExistsAsync(akeneoConnectionSettings, x => x.AkeneoConnectionPassword, storeScope);
            model.DefaultSyncProfileId_OverrideForStore = await settingService.SettingExistsAsync(akeneoConnectionSettings, x => x.DefaultSyncProfileId, storeScope);
        }

        await PrepareSyncContextOptionsAsync(model);

        return View("~/Plugins/Apt.Misc.AkeneoConnection/Views/Configure.cshtml", model);
    }

    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    [HttpPost("admin/akeneo-connection/configure")]
    public async Task<IActionResult> Configure(AkeneoConfigurationModel model)
    {
        var alertEmails = AkeneoSyncAlertService.ParseRecipients(model.SyncAlertEmails);

        if (model.SyncAlertsEnabled && alertEmails.Count == 0)
            ModelState.AddModelError(nameof(model.SyncAlertEmails), "Enter at least one valid email address to send sync alerts.");

        if (model.SyncAlertMinimumFailedProducts < 1)
            ModelState.AddModelError(nameof(model.SyncAlertMinimumFailedProducts), "Minimum failed products must be 1 or more.");

        if (model.SyncAlertStaleAfterHours < 0)
            ModelState.AddModelError(nameof(model.SyncAlertStaleAfterHours), "Stale sync hours must be 0 or more.");

        if (!ModelState.IsValid)
        {
            // Configure() reloads the saved values, so say what was rejected.
            notificationService.ErrorNotification(string.Join(" ", ModelState.Values
                .SelectMany(value => value.Errors)
                .Select(error => error.ErrorMessage)));
            return await Configure();
        }

        //load settings for a chosen store scope
        var storeScope = await storeContext.GetActiveStoreScopeConfigurationAsync();
        var akeneoConnectionSettings = await settingService.LoadSettingAsync<AkeneoConnectionSettings>(storeScope);

        //save settings
        akeneoConnectionSettings.AkeneoConnectionBaseUrl = model.AkeneoConnectionBaseUrl;
        akeneoConnectionSettings.AkeneoConnectionClientId = model.AkeneoConnectionClientId;
        akeneoConnectionSettings.AkeneoConnectionClientSecret = model.AkeneoConnectionClientSecret;
        akeneoConnectionSettings.AkeneoConnectionUsername = model.AkeneoConnectionUsername;
        akeneoConnectionSettings.AkeneoConnectionPassword = model.AkeneoConnectionPassword;
        akeneoConnectionSettings.DefaultSyncProfileId = model.DefaultSyncProfileId;

        /* We do not clear cache after each setting update.
         * This behavior can increase performance because cached settings will not be cleared
         * and loaded from database after each update */
        await settingService.SaveSettingOverridablePerStoreAsync(akeneoConnectionSettings, x => x.AkeneoConnectionBaseUrl, model.AkeneoConnectionBaseUrl_OverrideForStore, storeScope, false);
        await settingService.SaveSettingOverridablePerStoreAsync(akeneoConnectionSettings, x => x.AkeneoConnectionClientId, model.AkeneoConnectionClientId_OverrideForStore, storeScope, false);
        await settingService.SaveSettingOverridablePerStoreAsync(akeneoConnectionSettings, x => x.AkeneoConnectionClientSecret, model.AkeneoConnectionClientSecret_OverrideForStore, storeScope, false);
        await settingService.SaveSettingOverridablePerStoreAsync(akeneoConnectionSettings, x => x.AkeneoConnectionUsername, model.AkeneoConnectionUsername_OverrideForStore, storeScope, false);
        await settingService.SaveSettingOverridablePerStoreAsync(akeneoConnectionSettings, x => x.AkeneoConnectionPassword, model.AkeneoConnectionPassword_OverrideForStore, storeScope, false);
        await settingService.SaveSettingOverridablePerStoreAsync(akeneoConnectionSettings, x => x.DefaultSyncProfileId, model.DefaultSyncProfileId_OverrideForStore, storeScope, false);

        var alertSettings = await settingService.LoadSettingAsync<AkeneoConnectionSettings>();
        alertSettings.SyncAlertsEnabled = model.SyncAlertsEnabled;
        alertSettings.SyncAlertEmails = string.Join(", ", alertEmails);
        alertSettings.SyncAlertMinimumFailedProducts = model.SyncAlertMinimumFailedProducts;
        alertSettings.SyncAlertStaleAfterHours = model.SyncAlertStaleAfterHours;
        await settingService.SaveSettingAsync(alertSettings, x => x.SyncAlertsEnabled, 0, false);
        await settingService.SaveSettingAsync(alertSettings, x => x.SyncAlertEmails, 0, false);
        await settingService.SaveSettingAsync(alertSettings, x => x.SyncAlertMinimumFailedProducts, 0, false);
        await settingService.SaveSettingAsync(alertSettings, x => x.SyncAlertStaleAfterHours, 0, false);
        //now clear settings cache
        await settingService.ClearCacheAsync();

        notificationService.SuccessNotification(await localizationService.GetResourceAsync("Admin.Plugins.Saved"));

        return RedirectToAction("Configure");
    }

    #endregion
}
