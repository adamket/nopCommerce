using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Binding;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Services.Security;
using Nop.Web.Areas.Admin.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Controllers;

public class AkeneoCatalogBindingController(
    IAkeneoCatalogBindingService catalogBindingService,
    IAkeneoApiClient akeneoApiClient) : BaseAdminController
{
    private static readonly JsonSerializerOptions ReportJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [HttpGet("admin/akeneo-connection/catalog-binding")]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var families = new List<object>();
        string familyLoadError = null;

        try
        {
            families = (await akeneoApiClient.GetFamiliesAsync(cancellationToken: cancellationToken))
                .OrderBy(family => family.GetLabel())
                .Select(family => (object)new
                {
                    value = family.Code,
                    text = $"{family.GetLabel()} ({family.Code})"
                })
                .ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            familyLoadError = "Akeneo families could not be loaded: " + ex.Message;
        }

        ViewBag.Families = families;
        ViewBag.FamilyLoadError = familyLoadError;

        return View($"{AkeneoConnectionConstants.PathToPlugin}/Views/CatalogBinding.cshtml");
    }

    /// <param name="familyCodesJson">JSON array of Akeneo family codes; empty for the whole catalog.</param>
    [HttpPost]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public async Task<IActionResult> Scan(
        string familyCodesJson,
        CancellationToken cancellationToken)
    {
        try
        {
            var report = await catalogBindingService.ScanAsync(
                ParseFamilyCodes(familyCodesJson),
                cancellationToken);

            return Report(report);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Report(new AkeneoCatalogBindingReport
            {
                Success = false,
                Message = "Catalog binding scan failed. " + ex.Message
            }, StatusCodes.Status500InternalServerError);
        }
    }

    /// <param name="familyCodesJson">JSON array of Akeneo family codes; empty for the whole catalog.</param>
    /// <param name="conflictChoicesJson">JSON object: product model code → chosen nopCommerce parent product id.</param>
    [HttpPost]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public async Task<IActionResult> Commit(
        string familyCodesJson,
        string conflictChoicesJson,
        CancellationToken cancellationToken)
    {
        var report = await catalogBindingService.CommitAsync(
            ParseFamilyCodes(familyCodesJson),
            ParseConflictChoices(conflictChoicesJson),
            cancellationToken);

        if (report.AlreadyRunning)
            return Report(report, StatusCodes.Status409Conflict);

        return report.Success
            ? Report(report)
            : Report(report, StatusCodes.Status500InternalServerError);
    }

    private ContentResult Report(
        AkeneoCatalogBindingReport report,
        int statusCode = StatusCodes.Status200OK) =>
        new()
        {
            Content = JsonSerializer.Serialize(report, ReportJsonOptions),
            ContentType = "application/json",
            StatusCode = statusCode
        };

    private static List<string> ParseFamilyCodes(string json) =>
        string.IsNullOrWhiteSpace(json)
            ? new List<string>()
            : JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();

    private static Dictionary<string, int> ParseConflictChoices(string json) =>
        string.IsNullOrWhiteSpace(json)
            ? new Dictionary<string, int>()
            : JsonSerializer.Deserialize<Dictionary<string, int>>(json) ??
              new Dictionary<string, int>();
}
