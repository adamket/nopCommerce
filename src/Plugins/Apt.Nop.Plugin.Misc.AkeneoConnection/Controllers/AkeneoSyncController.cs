using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
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
public class AkeneoSyncController(
    IAkeneoProductMappingFactory akeneoProductMappingFactory,
    IAkeneoProductImportService akeneoProductImportService,
    IAkeneoSyncRunRecordService syncRunRecordService) : BasePluginController
{
    private const string DryRunViewPath =
        "~/Plugins/Apt.Misc.AkeneoConnection/Views/DryRun.cshtml";

    [HttpGet]
    public IActionResult DryRun()
    {
        return View(DryRunViewPath, new AkeneoProductMappingPreviewModel
        {
            Locale = "en_US",
            Channel = "ecommerce",
            Currency = "USD"
        });
    }

    [HttpPost]
    public async Task<IActionResult> DryRun(
        AkeneoProductMappingPreviewModel input,
        CancellationToken cancellationToken)
    {
        input ??= new AkeneoProductMappingPreviewModel();

        input.Locale = Normalize(input.Locale, "en_US");
        input.Channel = Normalize(input.Channel, "ecommerce");
        input.Currency = Normalize(input.Currency, "USD");

        if (string.IsNullOrWhiteSpace(input.AkeneoIdentifier))
        {
            input.HasSearched = true;
            input.Errors.Add("Akeneo identifier/SKU is required.");

            return View(DryRunViewPath, input);
        }

        var model = await akeneoProductMappingFactory.PreviewProductMappingAsync(
            input.AkeneoIdentifier,
            input.Locale,
            input.Channel,
            input.Currency,
            cancellationToken);

        model.Locale = input.Locale;
        model.Channel = input.Channel;
        model.Currency = input.Currency;

        return View(DryRunViewPath, model);
    }


    [HttpPost]
    public async Task<IActionResult> ImportAllProducts(
        CancellationToken cancellationToken)
    {
        var syncRun = new AkeneoSyncRunRecord
        {
            SyncTypeId = (int)SyncType.ManualProductSync,
            StartedOnUtc = DateTime.UtcNow,
            SyncStatusId = (int)SyncStatus.Started
        };

        await syncRunRecordService.InsertAkeneoSyncRunRecordAsync(syncRun);

        var result = await akeneoProductImportService.ImportProductsAsync(
            new AkeneoProductBatchImportRequest
            {
                SyncRunRecordId = syncRun.Id,
                PageSize = 100,
                CreateNewProducts = true,
                UpdateExistingProducts = true,
                AddMappedCategories = true,
                AddMappedManufacturers = true,
                CreateMissingSpecificationAttributeOptions = true,
                CreateMissingProductAttributeValues = true,
                LogSkippedProducts = false,
                SaveRawPayloadSnapshot = false
            },
            cancellationToken);

        syncRun.FinishedOnUtc = DateTime.UtcNow;
        syncRun.TotalRead = result.TotalRead;
        syncRun.CreatedCount = result.CreatedCount;
        syncRun.UpdatedCount = result.UpdatedCount;
        syncRun.SkippedCount = result.SkippedCount;
        syncRun.FailedCount = result.FailedCount;
        syncRun.SyncStatusId = (int)result.SyncStatus;
        syncRun.ErrorSummary = result.Errors.Any()
            ? string.Join(" | ", result.Errors)
            : null;

        await syncRunRecordService.UpdateAkeneoSyncRunRecordAsync(syncRun);

        return Json(new
        {
            success = result.Success,
            result.TotalRead,
            result.CreatedCount,
            result.UpdatedCount,
            result.SkippedCount,
            result.FailedCount,
            result.WarningCount,
            result.Canceled,
            result.Messages,
            result.Errors,
            result.SkippedSkuSample
        });
    }

    [HttpPost]
    public async Task<IActionResult> ImportProduct(
        AkeneoProductImportModel input,
        CancellationToken cancellationToken)
    {
        if (input == null || string.IsNullOrWhiteSpace(input.Uuid))
        {
            return Json(new
            {
                success = false,
                action = "failed",
                errors = new[] { "Akeneo product UUID is required. Run the dry run first, then import from the preview result." }
            });
        }

        var syncRunRecord = new AkeneoSyncRunRecord
        {
            StartedOnUtc = DateTime.UtcNow,
            SyncTypeId = (int)SyncType.ManualProductSync,
            SyncStatusId = (int)SyncStatus.Started
        };

        await syncRunRecordService.InsertAkeneoSyncRunRecordAsync(syncRunRecord);

        var result = await akeneoProductImportService.ImportProductByUuidAsync(
            new AkeneoProductImportRequest
            {
                SyncRunRecordId = syncRunRecord.Id,
                AkeneoProductUuid = input.Uuid.Trim(),
                Locale = Normalize(input.Locale, "en_US"),
                Channel = Normalize(input.Channel, "ecommerce"),
                Currency = Normalize(input.Currency, "USD"),

                CreateNewProducts = true,
                UpdateExistingProducts = true,
                CreateMissingSpecificationAttributeOptions = true,
                CreateMissingProductAttributeValues = true,
                AddMappedCategories = true,
                AddMappedManufacturers = true,
                SaveRawPayloadSnapshot = false
            },
            cancellationToken);

        syncRunRecord.FinishedOnUtc = DateTime.UtcNow;
        syncRunRecord.SyncStatusId = (int)SyncStatus.Completed;

        await syncRunRecordService.UpdateAkeneoSyncRunRecordAsync(syncRunRecord);
        var action = result.Success ? result.ActionType.ToString() : "failed";
      
        return Json(new
        {
            success = result.Success,
            syncRunRecordId = syncRunRecord.Id,
            action = action.ToString(),
            productId = result.NopProductId,
            akeneoProductUuid = result.AkeneoProductUuid,
            akeneoIdentifier = result.AkeneoIdentifier,
            messages = result.Messages,
            warnings = result.Warnings,
            errors = result.Errors
        });
    }

    private static string Normalize(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim();
    }
}