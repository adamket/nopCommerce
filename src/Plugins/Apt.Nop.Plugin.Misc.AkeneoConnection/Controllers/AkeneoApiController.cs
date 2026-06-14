using System.Threading;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api;
using Microsoft.AspNetCore.Mvc;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Controllers;
public class AkeneoApiController(IAkeneoApiClient akeneoApiClient) : BaseController
{
    [AuthorizeAdmin]
    [HttpPost("admin/akeneo-api/test-connection")]
    public async Task<IActionResult> TestConnection(AkeneoApiCredentials apiCredentials, CancellationToken cancellationToken)
    {

        var result = await akeneoApiClient.TestConnectionAsync(
            apiCredentials, cancellationToken);

        return Json(new
        {
            success = result.Success,
            message = result.Message
        });
    }
}
