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
public class ConfigurationController : BasePluginController
{
    #region Fields

    private readonly ISettingService _settingService;
    private readonly IStoreContext _storeContext;
    private readonly INotificationService _notificationService;
    private readonly ILocalizationService _localizationService;
    private readonly ITwilioService _twilioService;

    #endregion

    #region Ctor

    public ConfigurationController(
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
        var model = new OtpConfigurationModel();

        var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
        var settings = await _settingService.LoadSettingAsync<OtpSettings>(storeScope);

        model.TwilioAccountSid = settings.TwilioAccountSid;
        model.TwilioAuthToken = settings.TwilioAuthToken;
        model.TwilioFromNumber = settings.TwilioFromNumber;
        model.OtpGenerationIntervalSeconds = settings.OtpGenerationIntervalSeconds;
        model.OtpValidationIntervalSeconds = settings.OtpValidationIntervalSeconds;
        model.OtpExpiresAfterMinutes = settings.OtpExpiresAfterMinutes;
        model.AlwaysForwardToOtpInput = settings.AlwaysForwardToOtpInput;


        return View("~/Plugins/Apt.Misc.OneTimePasscode/Views/Configuration/Configure.cshtml", model);
    }

    [HttpPost("/admin/apt/otp/configure")]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public async Task<IActionResult> Configure(OtpConfigurationModel model)
    {
        if (!ModelState.IsValid)
            return await Configure();

        var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
        var settings = await _settingService.LoadSettingAsync<OtpSettings>(storeScope);

        settings.TwilioAccountSid = model.TwilioAccountSid ?? string.Empty;
        settings.TwilioAuthToken = model.TwilioAuthToken ?? string.Empty;
        settings.TwilioFromNumber = model.TwilioFromNumber ?? string.Empty;
        settings.OtpGenerationIntervalSeconds = model.OtpGenerationIntervalSeconds;
        settings.OtpValidationIntervalSeconds = model.OtpValidationIntervalSeconds;
        settings.OtpExpiresAfterMinutes = model.OtpExpiresAfterMinutes;
        settings.AlwaysForwardToOtpInput = model.AlwaysForwardToOtpInput;

        await _settingService.SaveSettingAsync(settings);

        await _settingService.ClearCacheAsync();

        var savedMessage = await _localizationService.GetResourceAsync("Admin.Plugins.Saved");
        _notificationService.SuccessNotification(savedMessage);


      //  await _twilioService.SendSmsAsync(settings.TwilioFromNumber, "8777804236", $"Your one time passcode is {Guid.NewGuid()}");
        return await Configure();
    }

    #endregion
}
