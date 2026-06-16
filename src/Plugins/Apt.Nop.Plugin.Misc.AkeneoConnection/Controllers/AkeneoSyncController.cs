using Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Microsoft.AspNetCore.Mvc;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Controllers;
[AuthorizeAdmin]
[Area(AreaNames.ADMIN)]
[AutoValidateAntiforgeryToken]
public class AkeneoSyncController(IAkeneoProductMappingFactory akeneoProductMappingService) : BasePluginController
{
    public IActionResult DryRun()
    {
        return View("~/Plugins/Apt.Misc.AkeneoConnection/Views/DryRun.cshtml",
            new AkeneoProductMappingPreviewModel());
    }

    [HttpPost]
    public async Task<IActionResult> DryRun(string akeneoIdentifier)
    {
        var model = await akeneoProductMappingService
            .PreviewProductMappingAsync(akeneoIdentifier);

        return View("~/Plugins/Apt.Misc.AkeneoConnection/Views/DryRun.cshtml", model);
    }
}