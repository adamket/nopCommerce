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
          OtpRequestIntervalSeconds = 30,
          OtpValidationIntervalSeconds = 5,
          OtpExpiresAfterMinutes = 15,
          CodeDigitCount = 6,
          PreventUserEnumeration = true,
          ShowDefaultOtpLoginButton = true,
          
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
