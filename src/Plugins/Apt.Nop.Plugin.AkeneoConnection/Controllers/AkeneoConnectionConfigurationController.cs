using Apt.Nop.Plugin.AkeneoConnection;
using Apt.Nop.Plugin.AkeneoConnection.Models;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Apt.Nop.Plugin.AkeneoConnection.Controllers;

[AuthorizeAdmin]
[Area(AreaNames.ADMIN)]
[AutoValidateAntiforgeryToken]
public class AkeneoConnectionConfigurationController : BasePaymentController
{
    #region Fields

    protected readonly ILanguageService _languageService;
    protected readonly ILocalizationService _localizationService;
    protected readonly INotificationService _notificationService;
    protected readonly IPermissionService _permissionService;
    protected readonly ISettingService _settingService;
    protected readonly IStoreContext _storeContext;

    #endregion

    #region Ctor

    public AkeneoConnectionConfigurationController(ILanguageService languageService,
        ILocalizationService localizationService,
        INotificationService notificationService,
        IPermissionService permissionService,
        ISettingService settingService,
        IStoreContext storeContext)
    {
        _languageService = languageService;
        _localizationService = localizationService;
        _notificationService = notificationService;
        _permissionService = permissionService;
        _settingService = settingService;
        _storeContext = storeContext;
    }

    #endregion

    #region Methods

    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public async Task<IActionResult> Configure()
    {
        //load settings for a chosen store scope
        var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
        var akeneoConnectionSettings = await _settingService.LoadSettingAsync<AkeneoConnectionSettings>(storeScope);

        var model = new AkeneoConfigurationModel
        {
            ActiveStoreScopeConfiguration = storeScope,
            AkeneoConnectionBaseUrl = akeneoConnectionSettings.AkeneoConnectionBaseUrl, 
            AkeneoConnectionClientId = akeneoConnectionSettings.AkeneoConnectionClientId,
            AkeneoConnectionClientSecret = akeneoConnectionSettings.AkeneoConnectionClientSecret,
            AkeneoConnectionUsername = akeneoConnectionSettings.AkeneoConnectionUsername,
            AkeneoConnectionPassword = akeneoConnectionSettings.AkeneoConnectionPassword,
            AkeneoConnectionChannel = akeneoConnectionSettings.AkeneoConnectionChannel,
            AkeneoConnectionLocale = akeneoConnectionSettings.AkeneoConnectionLocale
        };

        model.ActiveStoreScopeConfiguration = storeScope;
        if (storeScope > 0)
        {
            model.AkeneoConnectionBaseUrl_OverrideForStore = await _settingService.SettingExistsAsync(akeneoConnectionSettings, x => x.AkeneoConnectionBaseUrl, storeScope);
            model.AkeneoConnectionClientId_OverrideForStore = await _settingService.SettingExistsAsync(akeneoConnectionSettings, x => x.AkeneoConnectionClientId, storeScope);
            model.AkeneoConnectionClientSecret_OverrideForStore = await _settingService.SettingExistsAsync(akeneoConnectionSettings, x => x.AkeneoConnectionClientSecret, storeScope);
            model.AkeneoConnectionUsername_OverrideForStore = await _settingService.SettingExistsAsync(akeneoConnectionSettings, x => x.AkeneoConnectionUsername, storeScope);
            model.AkeneoConnectionPassword_OverrideForStore = await _settingService.SettingExistsAsync(akeneoConnectionSettings, x => x.AkeneoConnectionPassword, storeScope);
            model.AkeneoConnectionChannel_OverrideForStore = await _settingService.SettingExistsAsync(akeneoConnectionSettings, x => x.AkeneoConnectionChannel, storeScope);
            model.AkeneoConnectionLocale_OverrideForStore = await _settingService.SettingExistsAsync(akeneoConnectionSettings, x => x.AkeneoConnectionLocale, storeScope);
        }

        return View("~/Plugins/Apt.Misc.AkeneoConnection/Views/Configure.cshtml", model);
    }

    [HttpPost]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public async Task<IActionResult> Configure(AkeneoConfigurationModel model)
    {
        if (!ModelState.IsValid)
            return await Configure();

        //load settings for a chosen store scope
        var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
        var akeneoConnectionSettings = await _settingService.LoadSettingAsync<AkeneoConnectionSettings>(storeScope);

        //save settings
        akeneoConnectionSettings.AkeneoConnectionBaseUrl = model.AkeneoConnectionBaseUrl;
        akeneoConnectionSettings.AkeneoConnectionClientId = model.AkeneoConnectionClientId;
        akeneoConnectionSettings.AkeneoConnectionClientSecret = model.AkeneoConnectionClientSecret;
        akeneoConnectionSettings.AkeneoConnectionChannel = model.AkeneoConnectionChannel;
        akeneoConnectionSettings.AkeneoConnectionUsername = model.AkeneoConnectionUsername;
        akeneoConnectionSettings.AkeneoConnectionPassword = model.AkeneoConnectionPassword;
        akeneoConnectionSettings.AkeneoConnectionLocale = model.AkeneoConnectionLocale;


        /* We do not clear cache after each setting update.
         * This behavior can increase performance because cached settings will not be cleared 
         * and loaded from database after each update */
        await _settingService.SaveSettingOverridablePerStoreAsync(akeneoConnectionSettings, x => x.AkeneoConnectionBaseUrl, model.AkeneoConnectionBaseUrl_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(akeneoConnectionSettings, x => x.AkeneoConnectionClientId, model.AkeneoConnectionClientId_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(akeneoConnectionSettings, x => x.AkeneoConnectionClientSecret, model.AkeneoConnectionClientSecret_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(akeneoConnectionSettings, x => x.AkeneoConnectionUsername, model.AkeneoConnectionUsername_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(akeneoConnectionSettings, x => x.AkeneoConnectionPassword, model.AkeneoConnectionPassword_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(akeneoConnectionSettings, x => x.AkeneoConnectionChannel, model.AkeneoConnectionChannel_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(akeneoConnectionSettings, x => x.AkeneoConnectionLocale, model.AkeneoConnectionLocale_OverrideForStore, storeScope, false);

        //now clear settings cache
        await _settingService.ClearCacheAsync();

        //localization. no multi-store support for localization yet.
  
        _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Admin.Plugins.Saved"));

        return await Configure();
    }

    #endregion
}