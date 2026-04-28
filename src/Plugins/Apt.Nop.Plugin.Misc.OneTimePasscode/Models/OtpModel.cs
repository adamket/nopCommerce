namespace Apt.Nop.Plugin.Misc.OneTimePasscode.Models;

public class OtpModel
{
    public bool ShowDefaultOtpLoginButton { get; set; }
    public string PrimaryButtonColor { get; set; }
    public string PrimaryButtonHoverColor { get; set; }
    public string PrimaryButtonTextColor { get; set; }
    public string SecondaryButtonColor { get; set; }
    public string SecondaryButtonHoverColor { get; set; }
    public string SecondaryButtonTextColor { get; set; }

    public int ButtonBorderRadiusPx { get; set; }

    public CssType CssType { get; set; }
}