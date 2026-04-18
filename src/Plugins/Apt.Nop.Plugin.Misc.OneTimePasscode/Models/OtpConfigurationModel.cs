using System.ComponentModel.DataAnnotations;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Apt.Nop.Plugin.Misc.OneTimePasscode.Models;
public class OtpConfigurationModel
{
    public string TwilioAccountSid { get; set; } 
    public string TwilioAuthToken { get; set; } 
    public string TwilioFromNumber { get; set; }

    [Range(1, int.MaxValue)]
    [NopResourceDisplayName("Apt.Plugins.Misc.Otp.Settings.OtpValidationIntervalSeconds")]
    public int OtpValidationIntervalSeconds { get; set; }

    [Range(1, int.MaxValue)]
    [NopResourceDisplayName("Apt.Plugins.Misc.Otp.Settings.OtpGenerationIntervalSeconds")]
    public int OtpGenerationIntervalSeconds { get; set; }
    
    [Range(1, int.MaxValue)]
    [NopResourceDisplayName("Apt.Plugins.Misc.Otp.Settings.OtpExpiresAfterMinutes")]
    public int OtpExpiresAfterMinutes { get; set; }

    [NopResourceDisplayName("Apt.Plugins.Misc.Otp.Settings.AlwaysForwardToOtpInput")]
    public bool AlwaysForwardToOtpInput { get; set; }
}
