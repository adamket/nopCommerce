using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Apt.Nop.Plugin.Misc.OneTimePasscode.Models;

public class OtpConfigurationModel
{
    //public string TwilioAccountSid { get; set; } 
    //public string TwilioAuthToken { get; set; } 
    //public string TwilioFromNumber { get; set; }

    public int ActiveStoreScopeConfiguration { get; set; }

    [Range(1, int.MaxValue)]
    [NopResourceDisplayName("Apt.Plugins.Misc.Otp.Settings.OtpValidationIntervalSeconds")]
    public int OtpValidationIntervalSeconds { get; set; }

    public bool OtpValidationIntervalSeconds_OverrideForStore { get; set; }

    [Range(1, int.MaxValue)]
    //[NopResourceDisplayName("Apt.Plugins.Misc.Otp.Settings.OtpGenerationIntervalSeconds")]
    [NopResourceDisplayName("Apt.Plugins.Misc.Otp.Settings.OtpRequestIntervalSeconds")]
    public int OtpRequestIntervalSeconds { get; set; }

    public bool OtpRequestIntervalSeconds_OverrideForStore { get; set; }

    [Range(1, int.MaxValue)]
    [NopResourceDisplayName("Apt.Plugins.Misc.Otp.Settings.OtpExpiresAfterMinutes")]
    public int OtpExpiresAfterMinutes { get; set; }

    public bool OtpExpiresAfterMinutes_OverrideForStore { get; set; }

    [NopResourceDisplayName("Apt.Plugins.Misc.Otp.Settings.PreventUserEnumeration")]
    public bool PreventUserEnumeration { get; set; }

    public bool PreventUserEnumeration_OverrideForStore { get; set; }

    [NopResourceDisplayName("Apt.Plugins.Misc.Otp.Settings.CodeDigitCount")]
    public int CodeDigitCount { get; set; }

    public bool CodeDigitCount_OverrideForStore { get; set; }

    [NopResourceDisplayName("Apt.Plugins.Misc.Otp.Settings.ShowDefaultOtpLoginButton")]
    public bool ShowDefaultOtpLoginButton { get; set; }

    public bool ShowDefaultOtpLoginButton_OverrideForStore { get; set; }

    [NopResourceDisplayName("Apt.Plugins.Misc.Otp.Settings.PrimaryButtonColor")]
    [RegularExpression("^#([0-9A-Fa-f]{6})$", ErrorMessage = "Please enter a valid 6-digit hex color.")]
    public string PrimaryButtonColor { get; set; }

    public bool PrimaryButtonColor_OverrideForStore { get; set; }

    [NopResourceDisplayName("Apt.Plugins.Misc.Otp.Settings.PrimaryButtonHoverColor")]
    [RegularExpression("^#([0-9A-Fa-f]{6})$", ErrorMessage = "Please enter a valid 6-digit hex color.")]
    public string PrimaryButtonHoverColor { get; set; }

    public bool PrimaryButtonHoverColor_OverrideForStore { get; set; }

    [NopResourceDisplayName("Apt.Plugins.Misc.Otp.Settings.PrimaryButtonTextColor")]
    [RegularExpression("^#([0-9A-Fa-f]{6})$", ErrorMessage = "Please enter a valid 6-digit hex color.")]
    public string PrimaryButtonTextColor { get; set; }

    public bool PrimaryButtonTextColor_OverrideForStore { get; set; }

    [NopResourceDisplayName("Apt.Plugins.Misc.Otp.Settings.SecondaryButtonColor")]
    [RegularExpression("^#([0-9A-Fa-f]{6})$", ErrorMessage = "Please enter a valid 6-digit hex color.")]
    public string SecondaryButtonColor { get; set; }

    public bool SecondaryButtonColor_OverrideForStore { get; set; }

    [NopResourceDisplayName("Apt.Plugins.Misc.Otp.Settings.SecondaryButtonHoverColor")]
    [RegularExpression("^#([0-9A-Fa-f]{6})$", ErrorMessage = "Please enter a valid 6-digit hex color.")]
    public string SecondaryButtonHoverColor { get; set; }

    public bool SecondaryButtonHoverColor_OverrideForStore { get; set; }

    [NopResourceDisplayName("Apt.Plugins.Misc.Otp.Settings.SecondaryButtonTextColor")]
    [RegularExpression("^#([0-9A-Fa-f]{6})$", ErrorMessage = "Please enter a valid 6-digit hex color.")]
    public string SecondaryButtonTextColor { get; set; }

    public bool SecondaryButtonTextColor_OverrideForStore { get; set; }

    [NopResourceDisplayName("Apt.Plugins.Misc.Otp.Settings.ButtonBorderRadiusPx")]
    public int ButtonBorderRadiusPx { get; set; }
    public bool ButtonBorderRadiusPx_OverrideForStore { get; set; }


    public IList<SelectListItem> AvailableCodeDigitCounts { get; set; }
}