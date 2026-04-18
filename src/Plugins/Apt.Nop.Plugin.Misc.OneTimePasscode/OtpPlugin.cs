using System.Text;
using Apt.Nop.Plugin.Misc.OneTimePasscode.Components;
using Nop.Core;
using Nop.Core.Domain.Logging;
using Nop.Core.Domain.Messages;
using Nop.Core.Infrastructure;
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
    private readonly ILanguageService _languageService;
    private readonly INopFileProvider _fileProvider;
    #endregion

    #region Ctor

    public OtpPlugin(
        IWebHelper webHelper,
        ISettingService settingService,
        ILocalizationService localizationService,
        IMessageTemplateService messageTemplateService,
        ICustomerActivityService customerActivityService, IRepository<ActivityLogType> activityLogTypeRepository, ILanguageService languageService, INopFileProvider nopFileProvider)
    {
        _webHelper = webHelper;
        _settingService = settingService;
        _localizationService = localizationService;
        _messageTemplateService = messageTemplateService;
        _customerActivityService = customerActivityService;
        _activityLogTypeRepository = activityLogTypeRepository;
        _languageService = languageService;
        _fileProvider = nopFileProvider;
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

        var englishLanguage = (await _languageService.GetAllLanguagesAsync())
            .FirstOrDefault(x => "en".Equals(x.UniqueSeoCode, StringComparison.OrdinalIgnoreCase));

        if (englishLanguage != null)
        {
            var srFilePath = _fileProvider.MapPath($"{OtpConstants.PathToPlugin}/Localization/resources.en-us.xml");

            if (_fileProvider.FileExists(srFilePath))
            {
                await using var stream = File.OpenRead(srFilePath);
                using var sr = new StreamReader(stream, Encoding.UTF8);

                await _localizationService.ImportResourcesFromXmlAsync(englishLanguage, sr, true);
            }
        }

        //await _localizationService.AddOrUpdateLocaleResourceAsync("apt.plugins.misc.otp.Settings.OtpValidationIntervalSeconds", "OTP validation interval (seconds)");
        //await _localizationService.AddOrUpdateLocaleResourceAsync("apt.plugins.misc.otp.Settings.OtpValidationIntervalSeconds.Hint", "The number of seconds a user must wait between passcode validation attempts.");

        //await _localizationService.AddOrUpdateLocaleResourceAsync("apt.plugins.misc.otp.Settings.OtpGenerationIntervalSeconds", "OTP resend interval (seconds)");
        //await _localizationService.AddOrUpdateLocaleResourceAsync("apt.plugins.misc.otp.Settings.OtpGenerationIntervalSeconds.Hint", "The number of seconds a user must wait before requesting a new one-time passcode.");

        //await _localizationService.AddOrUpdateLocaleResourceAsync("apt.plugins.misc.otp.Settings.OtpExpiresAfterMinutes", "OTP expiration time (minutes)");
        //await _localizationService.AddOrUpdateLocaleResourceAsync("apt.plugins.misc.otp.Settings.OtpExpiresAfterMinutes.Hint", "The number of minutes before a generated passcode expires.");

        //await _localizationService.AddOrUpdateLocaleResourceAsync("apt.plugins.misc.otp.Settings.AlwaysForwardToOtpInput", "Always forward to passcode entry");
        //await _localizationService.AddOrUpdateLocaleResourceAsync("apt.plugins.misc.otp.Settings.AlwaysForwardToOtpInput.Hint", "If enabled, users will be directed to passcode entry even if customer with matching email does not exist.");

        //await _localizationService.AddOrUpdateLocaleResourceAsync("apt.plugins.misc.otp.page.open-modal-button.text", "Login with one-time code");
        //await _localizationService.AddOrUpdateLocaleResourceAsync("apt.plugins.misc.otp.modal.prompt", "Enter the 6-digit code sent to your email.");
        //await _localizationService.AddOrUpdateLocaleResourceAsync("apt.plugins.misc.otp.modal.title", "One-time passcode login");
        //await _localizationService.AddOrUpdateLocaleResourceAsync("apt.plugins.misc.otp.modal.resend-code-button.text", "Resend code");
        //await _localizationService.AddOrUpdateLocaleResourceAsync("apt.plugins.misc.otp.modal.wrong-email.prompt", "Wrong email?");
        //await _localizationService.AddOrUpdateLocaleResourceAsync("apt.plugins.misc.otp.modal.change-email.prompt", "Change it here");
        //await _localizationService.AddOrUpdateLocaleResourceAsync("apt.plugins.misc.otp.modal.email.prompt", "Enter your email below and submit to receive a one-time passcode to log into your account.");
        //await _localizationService.AddOrUpdateLocaleResourceAsync("apt.plugins.misc.otp.modal.forego-code-generation.text", "Already have a code?");
        //await _localizationService.AddOrUpdateLocaleResourceAsync("apt.plugins.misc.otp.modal.email.submit.text", "Submit");
        //await _localizationService.AddOrUpdateLocaleResourceAsync("apt.plugins.misc.otp.modal.login-button.text", "Login");
        //await _localizationService.AddOrUpdateLocaleResourceAsync("apt.plugins.misc.otp.modal.resend-try-again-button.text", "Can resend again in {0}s");


        //apt.plugins.misc.otp.activity-log.public-store.otp-requested
        //apt.plugins.misc.otp.activity-log.public-store.otp-login
        //apt.plugins.misc.otp.notification.logged-in
        //apt.plugins.misc.otp.errors.requested-validation-too-soon
        //apt.plugins.misc.otp.errors.otp-expired
        //apt.plugins.misc.otp.errors.incorrect-code
        await base.InstallAsync();
    }

    public override async Task UninstallAsync()
    {
        var otpTemplates = await _messageTemplateService.GetMessageTemplatesByNameAsync(OtpConstants.MessageTemplateSystemName);
        foreach (var template in otpTemplates)
        {
            await _messageTemplateService.DeleteMessageTemplateAsync(template);
        }

        await _localizationService.DeleteLocaleResourcesAsync("apt.plugins.misc.otp");

        // remove activity type
        //var activityType = await _customerActivityService.GetActivityTypeBySystemKeywordAsync(OtpConstants.OtpLoginActivitySystemName);
        //if (activityType != null)
        //{
        //    await _customerActivityService.DeleteActivityTypeAsync(activityType);
        //}

        await base.UninstallAsync();
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
