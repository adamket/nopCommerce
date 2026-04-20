using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
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
    //[NopResourceDisplayName("Apt.Plugins.Misc.Otp.Settings.OtpGenerationIntervalSeconds")]
    [NopResourceDisplayName("Apt.Plugins.Misc.Otp.Settings.OtpRequestIntervalSeconds")]
    public int OtpRequestIntervalSeconds { get; set; }
    
    [Range(1, int.MaxValue)]
    [NopResourceDisplayName("Apt.Plugins.Misc.Otp.Settings.OtpExpiresAfterMinutes")]
    public int OtpExpiresAfterMinutes { get; set; }

    [NopResourceDisplayName("Apt.Plugins.Misc.Otp.Settings.PreventUserEnumeration")]
    public bool PreventUserEnumeration { get; set; }

    [NopResourceDisplayName("Apt.Plugins.Misc.Otp.Settings.CodeDigitCount")]
    public int CodeDigitCount { get; set; }
    public IList<SelectListItem> AvailableCodeDigitCounts { get; set; } 
}
