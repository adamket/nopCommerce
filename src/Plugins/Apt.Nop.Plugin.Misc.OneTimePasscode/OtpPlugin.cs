using Nop.Core;
using Nop.Core.Domain.Logging;
using Nop.Core.Domain.Messages;
using Nop.Data;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Messages;
using Nop.Services.Plugins;

namespace Apt.Nop.Plugin.Misc.OneTimePasscode;

/// <summary>
/// Represents the One Time Passcode plugin
/// </summary>
public class OtpPlugin : BasePlugin, IMiscPlugin
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

    /// <summary>
    /// Gets a configuration page URL
    /// </summary>
    public override string GetConfigurationPageUrl()
    {
        return $"{_webHelper.GetStoreLocation()}admin/apt/otp/configure";
    }

    /// <summary>
    /// Install the plugin
    /// </summary>
    public override async Task InstallAsync()
    {
        // message template for OTP
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

        // activity type for OTP login
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



        //apt.opt.modal.prompt
        //apt.opt.modal.title


        await base.InstallAsync();
    }

    /// <summary>
    /// Uninstall the plugin
    /// </summary>
    public override async Task UninstallAsync()
    {
        // remove message template
        //var otpTemplates = await _messageTemplateService.GetMessageTemplatesByNameAsync(OtpConstants.MessageTemplateSystemName);
        //if (otpTemplates != null)
        //{
        //    await _messageTemplateService.Del(otpTemplate);
        //}

        // remove activity type
        //var activityType = await _customerActivityService.GetActivityTypeBySystemKeywordAsync(OtpConstants.OtpLoginActivitySystemName);
        //if (activityType != null)
        //{
        //    await _customerActivityService.DeleteActivityTypeAsync(activityType);
        //}

        //await base.UninstallAsync();
    }

    #endregion
}
