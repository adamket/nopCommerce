using Apt.Nop.Plugin.Misc.OneTimePasscode.Models;
using Microsoft.AspNetCore.Mvc;
using Nop.Web.Framework.Components;

namespace Apt.Nop.Plugin.Misc.OneTimePasscode.Components;

public partial class OtpViewComponent : NopViewComponent
{
    private readonly OtpSettings _settings;
    public OtpViewComponent(OtpSettings settings)
    {
        _settings = settings;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var model = new OtpModel
        {
            ShowDefaultOtpLoginButton = _settings.ShowDefaultOtpLoginButton,
            PrimaryButtonColor = _settings.PrimaryButtonColor,
            SecondaryButtonColor = _settings.SecondaryButtonColor,
            PrimaryButtonHoverColor = _settings.PrimaryButtonHoverColor,
            SecondaryButtonHoverColor = _settings.SecondaryButtonHoverColor,
            PrimaryButtonTextColor = _settings.PrimaryButtonTextColor,
            SecondaryButtonTextColor = _settings.SecondaryButtonTextColor,
            ButtonBorderRadiusPx = _settings.ButtonBorderRadiusPx,
            CssType = (CssType)_settings.CssTypeId
        };

        return View(model);
    }
}