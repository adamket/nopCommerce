using Apt.Nop.Plugin.Misc.OneTimePasscode.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Core;
using Nop.Services;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Security;
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
    // private readonly ITwilioService _twilioService;

    #endregion

    #region Ctor

    public ConfigurationController(
        ISettingService settingService,
        IStoreContext storeContext,
        INotificationService notificationService,
        ILocalizationService localizationService /*, ITwilioService twilioService*/)
    {
        _settingService = settingService;
        _storeContext = storeContext;
        _notificationService = notificationService;
        _localizationService = localizationService;
        // _twilioService = twilioService;
    }

    #endregion

    #region Methods

    [HttpGet("/admin/apt/otp/configure")]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public async Task<IActionResult> Configure()
    {
        var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
        var model = new OtpConfigurationModel { ActiveStoreScopeConfiguration = storeScope };


        var settings = await _settingService.LoadSettingAsync<OtpSettings>(storeScope);
       
        //model.TwilioAccountSid = settings.TwilioAccountSid;
        //model.TwilioAuthToken = settings.TwilioAuthToken;
        //model.TwilioFromNumber = settings.TwilioFromNumber;
        model.OtpRequestIntervalSeconds = settings.OtpRequestIntervalSeconds;
        model.OtpValidationIntervalSeconds = settings.OtpValidationIntervalSeconds;
        model.OtpExpiresAfterMinutes = settings.OtpExpiresAfterMinutes;
        model.PreventUserEnumeration = settings.PreventUserEnumeration;
        model.CodeDigitCount = settings.CodeDigitCount;
        model.ShowDefaultOtpLoginButton = settings.ShowDefaultOtpLoginButton;

        model.PrimaryButtonColor = settings.PrimaryButtonColor;
        model.PrimaryButtonHoverColor = settings.PrimaryButtonHoverColor;
        model.PrimaryButtonTextColor = settings.PrimaryButtonTextColor;
        model.SecondaryButtonColor = settings.SecondaryButtonColor;
        model.SecondaryButtonHoverColor = settings.SecondaryButtonHoverColor;
        model.SecondaryButtonTextColor = settings.SecondaryButtonTextColor;
        model.CssTypeId = settings.CssTypeId;

        if (storeScope > 0)
        {
            model.OtpValidationIntervalSeconds_OverrideForStore =
                await _settingService.SettingExistsAsync(settings, x => x.OtpValidationIntervalSeconds, storeScope);
            model.OtpRequestIntervalSeconds_OverrideForStore =
                await _settingService.SettingExistsAsync(settings, x => x.OtpRequestIntervalSeconds, storeScope);
            model.OtpExpiresAfterMinutes_OverrideForStore =
                await _settingService.SettingExistsAsync(settings, x => x.OtpExpiresAfterMinutes, storeScope);
            model.PreventUserEnumeration_OverrideForStore =
                await _settingService.SettingExistsAsync(settings, x => x.PreventUserEnumeration, storeScope);
            model.ShowDefaultOtpLoginButton_OverrideForStore =
                await _settingService.SettingExistsAsync(settings, x => x.ShowDefaultOtpLoginButton, storeScope);
            model.CodeDigitCount_OverrideForStore =
                await _settingService.SettingExistsAsync(settings, x => x.CodeDigitCount, storeScope);
            model.CssTypeId_OverrideForStore =
                await _settingService.SettingExistsAsync(settings, x => x.CssTypeId, storeScope);

            model.PrimaryButtonColor_OverrideForStore =
                await _settingService.SettingExistsAsync(settings, x => x.PrimaryButtonColor, storeScope);
            model.PrimaryButtonHoverColor_OverrideForStore =
                await _settingService.SettingExistsAsync(settings, x => x.PrimaryButtonHoverColor, storeScope);
            model.PrimaryButtonTextColor_OverrideForStore =
                await _settingService.SettingExistsAsync(settings, x => x.PrimaryButtonTextColor, storeScope);
            model.SecondaryButtonColor_OverrideForStore =
                await _settingService.SettingExistsAsync(settings, x => x.SecondaryButtonColor, storeScope);
            model.SecondaryButtonHoverColor_OverrideForStore =
                await _settingService.SettingExistsAsync(settings, x => x.SecondaryButtonHoverColor, storeScope);
            model.SecondaryButtonTextColor_OverrideForStore =
                await _settingService.SettingExistsAsync(settings, x => x.SecondaryButtonTextColor, storeScope);
        }

        PrepareButtonStyleGroupModels(model);

        model.AvailableCodeDigitCounts = new List<SelectListItem>();
        for (var i = 3; i <= 6; ++i)
        {
            model.AvailableCodeDigitCounts.Add(new(i.ToString(), i.ToString()));
        }

        model.AvailableCssTypes = (await CssType.Custom.ToSelectListAsync()).ToList();


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

        //settings.TwilioAccountSid = model.TwilioAccountSid ?? string.Empty;
        //settings.TwilioAuthToken = model.TwilioAuthToken ?? string.Empty;
        //settings.TwilioFromNumber = model.TwilioFromNumber ?? string.Empty;
        settings.OtpRequestIntervalSeconds = model.OtpRequestIntervalSeconds;
        settings.OtpValidationIntervalSeconds = model.OtpValidationIntervalSeconds;
        settings.OtpExpiresAfterMinutes = model.OtpExpiresAfterMinutes;
        settings.PreventUserEnumeration = model.PreventUserEnumeration;
        settings.CodeDigitCount = model.CodeDigitCount;
        settings.ShowDefaultOtpLoginButton = model.ShowDefaultOtpLoginButton;

        settings.PrimaryButtonColor = model.PrimaryButtonColor;
        settings.PrimaryButtonHoverColor = model.PrimaryButtonHoverColor;
        settings.PrimaryButtonTextColor = model.PrimaryButtonTextColor;
        settings.SecondaryButtonColor = model.SecondaryButtonColor;
        settings.SecondaryButtonHoverColor = model.SecondaryButtonHoverColor;
        settings.SecondaryButtonTextColor = model.SecondaryButtonTextColor;
        settings.CssTypeId = model.CssTypeId;
     

        await _settingService.SaveSettingOverridablePerStoreAsync(
            settings, x => x.OtpRequestIntervalSeconds, model.OtpRequestIntervalSeconds_OverrideForStore, storeScope,
            false);
        await _settingService.SaveSettingOverridablePerStoreAsync(
            settings, x => x.OtpValidationIntervalSeconds, model.OtpValidationIntervalSeconds_OverrideForStore,
            storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(
            settings, x => x.OtpExpiresAfterMinutes, model.OtpExpiresAfterMinutes_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(
            settings, x => x.PreventUserEnumeration, model.PreventUserEnumeration_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(
            settings, x => x.CodeDigitCount, model.CodeDigitCount_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(
            settings, x => x.ShowDefaultOtpLoginButton, model.ShowDefaultOtpLoginButton_OverrideForStore, storeScope,
            false);

        await _settingService.SaveSettingOverridablePerStoreAsync(
            settings, x => x.PrimaryButtonColor, model.PrimaryButtonColor_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(
            settings, x => x.PrimaryButtonHoverColor, model.PrimaryButtonHoverColor_OverrideForStore, storeScope,
            false);
        await _settingService.SaveSettingOverridablePerStoreAsync(
            settings, x => x.PrimaryButtonTextColor, model.PrimaryButtonTextColor_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(
            settings, x => x.SecondaryButtonColor, model.SecondaryButtonColor_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(
            settings, x => x.SecondaryButtonHoverColor, model.SecondaryButtonHoverColor_OverrideForStore, storeScope,
            false);
        await _settingService.SaveSettingOverridablePerStoreAsync(
            settings, x => x.SecondaryButtonTextColor, model.SecondaryButtonTextColor_OverrideForStore, storeScope,
            false);
        await _settingService.SaveSettingOverridablePerStoreAsync(
            settings, x => x.CssTypeId, model.CssTypeId_OverrideForStore, storeScope,
            false);


        await _settingService.SaveSettingAsync(settings);
        await _settingService.ClearCacheAsync();

        var savedMessage = await _localizationService.GetResourceAsync("Admin.Plugins.Saved");
        _notificationService.SuccessNotification(savedMessage);

        // await _twilioService.SendSmsAsync(settings.TwilioFromNumber, "8777804236", $"Your one time passcode is {Guid.NewGuid()}");
        return await Configure();
    }



    public void PrepareButtonStyleGroupModels(OtpConfigurationModel model)
    {
        model.ButtonStyleGroups = new List<ButtonStyleGroupModel>
        {
            new()
            {
                TitleResourceKey = "Apt.Plugins.Misc.Otp.Settings.PrimaryButtonGroup",
                CssClass = "primary-btn-panel",
                Fields =
                    new List<StyleFieldModel>
                    {
                        new()
                        {
                            PropertyName = nameof(model.PrimaryButtonColor),
                            OverridePropertyName = nameof(model.PrimaryButtonColor_OverrideForStore),
                            LabelResourceKey = "Apt.Plugins.Misc.Otp.Settings.ButtonColor",
                            Value = model.PrimaryButtonColor
                        },
                        new()
                        {
                            PropertyName = nameof(model.PrimaryButtonHoverColor),
                            OverridePropertyName =
                                nameof(model.PrimaryButtonHoverColor_OverrideForStore),
                            LabelResourceKey = "Apt.Plugins.Misc.Otp.Settings.ButtonHoverColor",
                            Value = model.PrimaryButtonHoverColor
                        },
                        new()
                        {
                            PropertyName = nameof(model.PrimaryButtonTextColor),
                            OverridePropertyName =
                                nameof(model.PrimaryButtonTextColor_OverrideForStore),
                            LabelResourceKey = "Apt.Plugins.Misc.Otp.Settings.ButtonTextColor",
                            Value = model.PrimaryButtonTextColor
                        }
                    }
            },
            new()
            {
                TitleResourceKey = "Apt.Plugins.Misc.Otp.Settings.SecondaryButtonGroup",
                CssClass = "primary-btn-panel",
                Fields = new List<StyleFieldModel>
                {
                    new()
                    {
                        PropertyName = nameof(model.SecondaryButtonColor),
                        OverridePropertyName = nameof(model.SecondaryButtonColor_OverrideForStore),
                        LabelResourceKey = "Apt.Plugins.Misc.Otp.Settings.ButtonColor",
                        Value = model.SecondaryButtonColor
                    },
                    new()
                    {
                        PropertyName = nameof(model.SecondaryButtonHoverColor),
                        OverridePropertyName = nameof(model.SecondaryButtonHoverColor_OverrideForStore),
                        LabelResourceKey = "Apt.Plugins.Misc.Otp.Settings.ButtonHoverColor",
                        Value = model.SecondaryButtonHoverColor
                    },
                    new()
                    {
                        PropertyName = nameof(model.SecondaryButtonTextColor),
                        OverridePropertyName = nameof(model.SecondaryButtonTextColor_OverrideForStore),
                        LabelResourceKey = "Apt.Plugins.Misc.Otp.Settings.ButtonTextColor",
                        Value = model.SecondaryButtonTextColor
                    }
                }
            }
        };

    }
    #endregion
}