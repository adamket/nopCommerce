using Microsoft.AspNetCore.Mvc;
using Nop.Web.Framework.Components;

namespace Apt.Nop.Plugin.Misc.OneTimePasscode.Components;

public partial class OtpViewComponent : NopViewComponent
{

    public OtpViewComponent()
    {
    
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        
        return View($"{OtpConstants.PathToPlugin}/Views/Components/Otp/Default.cshtml");
    }
}