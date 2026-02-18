using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Apt.Nop.Plugin.Misc.OneTimePasscode.Models;
using Apt.Nop.Plugin.Misc.OneTimePasscode.Services;
using Nop.Core;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Security;
using Nop.Services.Stores;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Apt.Nop.Plugin.Misc.OneTimePasscode.Controllers;
[Area(AreaNames.ADMIN)]
[AuthorizeAdmin]
[AutoValidateAntiforgeryToken]
public class OneTimePasscodeController : BasePluginController
{
    #region Fields

    private readonly ISettingService _settingService;
    private readonly IStoreContext _storeContext;
    private readonly INotificationService _notificationService;
    private readonly ILocalizationService _localizationService;
    private readonly ITwilioService _twilioService;

    #endregion

    #region Ctor

    public OneTimePasscodeController(
        ISettingService settingService,
        IStoreContext storeContext,
        INotificationService notificationService,
        ILocalizationService localizationService, ITwilioService twilioService)
    {
        _settingService = settingService;
        _storeContext = storeContext;
        _notificationService = notificationService;
        _localizationService = localizationService;
        _twilioService = twilioService;
    }

    #endregion

    #region Methods

    [HttpGet("/admin/apt/otp/configure")]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public async Task<IActionResult> Configure()
    {
        var model = new OneTimePasscodeModel();

        // load settings for active store scope (0 = all stores)
        var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
        var settings = await _settingService.LoadSettingAsync<OneTimePasscodeSettings>(storeScope);

        model.TwilioAccountSid = settings.TwilioAccountSid;
        model.TwilioAuthToken = settings.TwilioAuthToken;
        model.TwilioFromNumber = settings.TwilioFromNumber;

        return View("~/Plugins/Apt.Misc.OneTimePasscode/Views/Configure.cshtml", model);
    }

    [HttpPost("/admin/apt/otp/configure")]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public async Task<IActionResult> Configure(OneTimePasscodeModel model)
    {
        if (!ModelState.IsValid)
            return await Configure();

        var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
        var settings = await _settingService.LoadSettingAsync<OneTimePasscodeSettings>(storeScope);

        settings.TwilioAccountSid = model.TwilioAccountSid ?? string.Empty;
        settings.TwilioAuthToken = model.TwilioAuthToken ?? string.Empty;
        settings.TwilioFromNumber = model.TwilioFromNumber ?? string.Empty;

        // save settings for the store scope
        await _settingService.SaveSettingAsync(settings);

        // clear settings cache
        await _settingService.ClearCacheAsync();

        // notify admin
        var savedMessage = await _localizationService.GetResourceAsync("Admin.Plugins.Saved");
        _notificationService.SuccessNotification(savedMessage);


        await _twilioService.SendSmsAsync(settings.TwilioFromNumber, "8777804236", $"Your one time passcode is {Guid.NewGuid()}");



        return await Configure();
    }

    #endregion
}
