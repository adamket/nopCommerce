using Apt.Nop.Plugin.Misc.OneTimePasscode.Components;
using Nop.Core;
using Nop.Core.Domain.Logging;
using Nop.Core.Domain.Messages;
using Nop.Data;
using Nop.Services.Cms;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Messages;
using Nop.Services.Plugins;

namespace Apt.Nop.Plugin.Misc.OneTimePasscode;

public class OtpPlugin : BasePlugin, IMiscPlugin, IWidgetPlugin
{
    #region Fields
    private readonly IWebHelper _webHelper;
    private readonly ISettingService _settingService;
    private readonly ILocalizationService _localizationService;
    private readonly IMessageTemplateService _messageTemplateService;
    private readonly ICustomerActivityService _customerActivityService;
    private readonly IRepository<ActivityLogType> _activityLogTypeRepository;
    #endregion

    #region Ctor

    public OtpPlugin(
        IWebHelper webHelper,
        ISettingService settingService,
        ILocalizationService localizationService,
        IMessageTemplateService messageTemplateService,
        ICustomerActivityService customerActivityService, IRepository<ActivityLogType> activityLogTypeRepository)
    {
        _webHelper = webHelper;
        _settingService = settingService;
        _localizationService = localizationService;
        _messageTemplateService = messageTemplateService;
        _customerActivityService = customerActivityService;
        _activityLogTypeRepository = activityLogTypeRepository;
    }

    #endregion

    #region Methods


    public override string GetConfigurationPageUrl()
    {
        return $"{_webHelper.GetStoreLocation()}admin/apt/otp/configure";
    }

    public override async Task InstallAsync()
    {

        //settings
        var settings = new OtpSettings()
        {
          OtpGenerationIntervalSeconds = 30,
          OtpValidationIntervalSeconds = 5,
          AlwaysForwardToOtpInput = true,
          OtpExpiresAfterMinutes = 5
        };
        await _settingService.SaveSettingAsync(settings);

        var otpTemplate = (await _messageTemplateService.GetMessageTemplatesByNameAsync(OtpConstants.MessageTemplateSystemName)).FirstOrDefault();
        if (otpTemplate == null)
        {
            otpTemplate = new MessageTemplate
            {
                Name = OtpConstants.MessageTemplateSystemName,
                Subject = "%Store.Name% - One-time passcode",
                Body = "Your one-time passcode is: %Otp.Code%",
                IsActive = true,
                DelayBeforeSend = null
            };

            await _messageTemplateService.InsertMessageTemplateAsync(otpTemplate);
        }

        var activityType =
            _activityLogTypeRepository.Table.FirstOrDefault(q =>
                q.SystemKeyword == OtpConstants.OtpLoginActivitySystemName);

        if (activityType == null)
        {
            activityType = new ActivityLogType
            {
                SystemKeyword = OtpConstants.OtpLoginActivitySystemName,
                Name = "Customer login with one-time passcode",
                Enabled = true
            };
            await _activityLogTypeRepository.InsertAsync(activityType);
        }

        await _localizationService.AddOrUpdateLocaleResourceAsync("Apt.Plugins.Misc.Otp.Settings.OtpValidationIntervalSeconds", "OTP validation interval (seconds)");
        await _localizationService.AddOrUpdateLocaleResourceAsync("Apt.Plugins.Misc.Otp.Settings.OtpValidationIntervalSeconds.Hint", "The number of seconds a user must wait between passcode validation attempts.");

        await _localizationService.AddOrUpdateLocaleResourceAsync("Apt.Plugins.Misc.Otp.Settings.OtpGenerationIntervalSeconds", "OTP resend interval (seconds)");
        await _localizationService.AddOrUpdateLocaleResourceAsync("Apt.Plugins.Misc.Otp.Settings.OtpGenerationIntervalSeconds.Hint", "The number of seconds a user must wait before requesting a new one-time passcode.");

        await _localizationService.AddOrUpdateLocaleResourceAsync("Apt.Plugins.Misc.Otp.Settings.OtpExpiresAfterMinutes", "OTP expiration time (minutes)");
        await _localizationService.AddOrUpdateLocaleResourceAsync("Apt.Plugins.Misc.Otp.Settings.OtpExpiresAfterMinutes.Hint", "The number of minutes before a generated passcode expires.");

        await _localizationService.AddOrUpdateLocaleResourceAsync("Apt.Plugins.Misc.Otp.Settings.AlwaysForwardToOtpInput", "Always forward to passcode entry");
        await _localizationService.AddOrUpdateLocaleResourceAsync("Apt.Plugins.Misc.Otp.Settings.AlwaysForwardToOtpInput.Hint", "If enabled, users will be directed to passcode entry even if customer does not exist.");

        await _localizationService.AddOrUpdateLocaleResourceAsync("apt.plugins.misc.otp.modal.prompt", "Enter the 6-digit code sent to your email.");
        await _localizationService.AddOrUpdateLocaleResourceAsync("apt.plugins.misc.otp.modal.title", "One-time passcode login");
        await _localizationService.AddOrUpdateLocaleResourceAsync("apt.plugins.misc.otp.open-modal-button", "Login with one-time code");
        await base.InstallAsync();
    }

    public override async Task UninstallAsync()
    {
        //remove message template
        var otpTemplates = await _messageTemplateService.GetMessageTemplatesByNameAsync(OtpConstants.MessageTemplateSystemName);
        foreach (var template in otpTemplates)
        {
            await _messageTemplateService.DeleteMessageTemplateAsync(template);
        }


        // remove activity type
        //var activityType = await _customerActivityService.GetActivityTypeBySystemKeywordAsync(OtpConstants.OtpLoginActivitySystemName);
        //if (activityType != null)
        //{
        //    await _customerActivityService.DeleteActivityTypeAsync(activityType);
        //}

        //await base.UninstallAsync();
    }

    #endregion

    public bool HideInWidgetList => false;
    public async Task<IList<string>> GetWidgetZonesAsync()
    {
        return new List<string> { "login_bottom" };
    }

    public Type GetWidgetViewComponent(string widgetZone)
    {
        return typeof(OtpViewComponent);
    }
}
