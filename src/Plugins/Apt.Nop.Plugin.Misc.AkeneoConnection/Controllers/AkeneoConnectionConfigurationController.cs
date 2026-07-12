using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api;
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
            //DefaultChannelCode = akeneoConnectionSettings.DefaultChannelCode,
            //DefaultLocaleCode = akeneoConnectionSettings.DefaultLocaleCode,
            //DefaultCurrencyCode = akeneoConnectionSettings.DefaultCurrencyCode
        };

        if (storeScope > 0)
        {
            model.AkeneoConnectionBaseUrl_OverrideForStore = await settingService.SettingExistsAsync(akeneoConnectionSettings, x => x.AkeneoConnectionBaseUrl, storeScope);
            model.AkeneoConnectionClientId_OverrideForStore = await settingService.SettingExistsAsync(akeneoConnectionSettings, x => x.AkeneoConnectionClientId, storeScope);
            model.AkeneoConnectionClientSecret_OverrideForStore = await settingService.SettingExistsAsync(akeneoConnectionSettings, x => x.AkeneoConnectionClientSecret, storeScope);
            model.AkeneoConnectionUsername_OverrideForStore = await settingService.SettingExistsAsync(akeneoConnectionSettings, x => x.AkeneoConnectionUsername, storeScope);
            model.AkeneoConnectionPassword_OverrideForStore = await settingService.SettingExistsAsync(akeneoConnectionSettings, x => x.AkeneoConnectionPassword, storeScope);
            model.DefaultSyncProfileId_OverrideForStore = await settingService.SettingExistsAsync(akeneoConnectionSettings, x => x.DefaultSyncProfileId, storeScope);
            
            
            //model.DefaultChannelCode_OverrideForStore = await settingService.SettingExistsAsync(akeneoConnectionSettings, x => x.DefaultChannelCode, storeScope);
            //model.DefaultLocaleCode_OverrideForStore = await settingService.SettingExistsAsync(akeneoConnectionSettings, x => x.DefaultLocaleCode, storeScope);
            //model.DefaultCurrencyCode_OverrideForStore = await settingService.SettingExistsAsync(akeneoConnectionSettings, x => x.DefaultCurrencyCode, storeScope);
        }

        await PrepareSyncContextOptionsAsync(model);

        return View("~/Plugins/Apt.Misc.AkeneoConnection/Views/Configure.cshtml", model);
    }

    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    [HttpPost("admin/akeneo-connection/configure")]
    public async Task<IActionResult> Configure(AkeneoConfigurationModel model)
    {
        if (!ModelState.IsValid)
            return await Configure();

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
        //akeneoConnectionSettings.DefaultLocaleCode = model.DefaultLocaleCode;
        //akeneoConnectionSettings.DefaultChannelCode = model.DefaultChannelCode;
        //akeneoConnectionSettings.DefaultCurrencyCode = model.DefaultCurrencyCode;

        /* We do not clear cache after each setting update.
         * This behavior can increase performance because cached settings will not be cleared
         * and loaded from database after each update */
        await settingService.SaveSettingOverridablePerStoreAsync(akeneoConnectionSettings, x => x.AkeneoConnectionBaseUrl, model.AkeneoConnectionBaseUrl_OverrideForStore, storeScope, false);
        await settingService.SaveSettingOverridablePerStoreAsync(akeneoConnectionSettings, x => x.AkeneoConnectionClientId, model.AkeneoConnectionClientId_OverrideForStore, storeScope, false);
        await settingService.SaveSettingOverridablePerStoreAsync(akeneoConnectionSettings, x => x.AkeneoConnectionClientSecret, model.AkeneoConnectionClientSecret_OverrideForStore, storeScope, false);
        await settingService.SaveSettingOverridablePerStoreAsync(akeneoConnectionSettings, x => x.AkeneoConnectionUsername, model.AkeneoConnectionUsername_OverrideForStore, storeScope, false);
        await settingService.SaveSettingOverridablePerStoreAsync(akeneoConnectionSettings, x => x.AkeneoConnectionPassword, model.AkeneoConnectionPassword_OverrideForStore, storeScope, false);
        await settingService.SaveSettingOverridablePerStoreAsync(akeneoConnectionSettings, x => x.DefaultSyncProfileId, model.DefaultSyncProfileId_OverrideForStore, storeScope, false);
        //await settingService.SaveSettingOverridablePerStoreAsync(akeneoConnectionSettings, x => x.DefaultChannelCode, model.DefaultChannelCode_OverrideForStore, storeScope, false);
        //await settingService.SaveSettingOverridablePerStoreAsync(akeneoConnectionSettings, x => x.DefaultLocaleCode, model.DefaultLocaleCode_OverrideForStore, storeScope, false);
        //await settingService.SaveSettingOverridablePerStoreAsync(akeneoConnectionSettings, x => x.DefaultCurrencyCode, model.DefaultCurrencyCode_OverrideForStore, storeScope, false);

        //now clear settings cache
        await settingService.ClearCacheAsync();

        notificationService.SuccessNotification(await localizationService.GetResourceAsync("Admin.Plugins.Saved"));

        return RedirectToAction("Configure");
    }

    #endregion
}