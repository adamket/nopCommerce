using Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Microsoft.AspNetCore.Mvc;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Controllers;
[AuthorizeAdmin]
[Area(AreaNames.ADMIN)]
[AutoValidateAntiforgeryToken]
public class AkeneoSyncController(IAkeneoProductMappingFactory akeneoProductMappingService, IAkeneoProductImportService akeneoProductImportService) : BasePluginController
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

    [HttpPost]
    public async Task<IActionResult> ImportProduct(string uuid, string locale, string channel, string currency)
    {
        var syncRunId = Guid.NewGuid().ToString("N");

        var result = await akeneoProductImportService.ImportProductByUuidAsync(
            new AkeneoProductImportRequest
            {
                SyncRunId = syncRunId,
                AkeneoProductUuid = uuid,
                Locale = locale,
                Channel = channel,
                Currency = currency,
                CreateNewProducts = true,
                UpdateExistingProducts = true,
                CreateMissingSpecificationAttributeOptions = true,
                CreateMissingProductAttributeValues = true,
                AddMappedCategories = true,
                AddMappedManufacturers = true,
                SaveRawPayloadSnapshot = false
            });

        return Json(new
        {
            success = result.Success,
            syncRunId,
            action = result.Created
                ? "created"
                : result.Updated
                    ? "updated"
                    : result.Skipped
                        ? "skipped"
                        : "failed",
            productId = result.NopProductId,
            akeneoProductUuid = result.AkeneoProductUuid,
            akeneoIdentifier = result.AkeneoIdentifier,
            messages = result.Messages,
            warnings = result.Warnings,
            errors = result.Errors
        });
    }




}