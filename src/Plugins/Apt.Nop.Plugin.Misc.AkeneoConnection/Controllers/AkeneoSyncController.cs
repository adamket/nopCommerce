using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Humanizer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Services.Messages;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Controllers;

[AuthorizeAdmin]
[Area(AreaNames.ADMIN)]
[AutoValidateAntiforgeryToken]
public class AkeneoSyncController(
    IAkeneoProductImportExecutionService productImportExecutionService,
    IAkeneoProductMappingFactory productMappingFactory,
    IAkeneoProductImportService productImportService,
    IAkeneoSyncRunRecordService syncRunRecordService, IAkeneoProductBatchImportRequestFactory productBatchImportRequestFactory, IAkeneoSyncProfileService syncProfileService) : BasePluginController
{
    private const string DryRunViewPath =
        "~/Plugins/Apt.Misc.AkeneoConnection/Views/DryRun.cshtml";

    [HttpGet("admin/akeneo-connection/dry-run")]
    public IActionResult DryRun()
    {
        return View(DryRunViewPath, new AkeneoProductMappingPreviewModel
        {
            Locale = "en_US",
            Channel = "ecommerce",
            Currency = "USD"
        });
    }

    [HttpPost("admin/akeneo-connection/dry-run")]
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

        var model = await productMappingFactory.PreviewProductMappingAsync(
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

        var result = await productImportService.ImportProductByUuidAsync(
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


    [HttpPost]
    public async Task<IActionResult> ImportProductsByProfile(
        int id,
        CancellationToken cancellationToken)
    {
        var result = await productImportExecutionService.ImportProductsByProfileAsync(
            id,
            SyncType.ManualProductSync,
            cancellationToken);

        if (result.ProfileNotFound)
        {
            return NotFound(new
            {
                success = false,
                message = result.Message
            });
        }

        if (result.ProfileDisabled)
        {
            return BadRequest(new
            {
                success = false,
                message = result.Message,
                profileId = result.ProfileId
            });
        }

        if (result.SyncStatus == SyncStatus.Failed)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                message = result.Message,
                profileId = result.ProfileId,
                syncRunRecordId = result.SyncRunRecordId,
                syncStatusId = (int)result.SyncStatus,
                syncStatus = result.SyncStatus.ToString(),
                errors = result.Errors
            });
        }

        return Json(new
        {
            success = result.Success,
            canceled = result.Canceled,
            completedWithErrors = result.CompletedWithErrors,
            message = result.Message,
            profileId = result.ProfileId,
            syncRunRecordId = result.SyncRunRecordId,
            syncStatusId = (int)result.SyncStatus,
            syncStatus = result.SyncStatus.ToString(),
            counts = new
            {
                totalRead = result.TotalRead,
                created = result.CreatedCount,
                updated = result.UpdatedCount,
                skipped = result.SkippedCount,
                failed = result.FailedCount,
                warnings = result.WarningCount
            },
            errors = result.Errors,
            messages = result.Messages
        });
    }


    //private static void ApplyImportResultToRunRecord(
    //    AkeneoSyncRunRecord runRecord,
    //    AkeneoProductBatchImportResult result)
    //{
    //    runRecord.FinishedOnUtc = DateTime.UtcNow;
    //    runRecord.TotalRead = result.TotalRead;
    //    runRecord.CreatedCount = result.CreatedCount;
    //    runRecord.UpdatedCount = result.UpdatedCount;
    //    runRecord.SkippedCount = result.SkippedCount;
    //    runRecord.FailedCount = result.FailedCount;

    //    if (result.Canceled)
    //    {
    //        runRecord.SyncStatusId = (int)SyncStatus.Cancelled;
    //        runRecord.ErrorSummary = "Product import was canceled.";
    //        return;
    //    }

    //    if (result.Errors.Any() || result.FailedCount > 0)
    //    {
    //        runRecord.SyncStatusId = (int)SyncStatus.CompletedWithErrors;
    //        runRecord.ErrorSummary = string.Join(Environment.NewLine, result.Errors).Truncate(4000);
    //        return;
    //    }

    //    runRecord.SyncStatusId = (int)SyncStatus.Completed;
    //    runRecord.ErrorSummary = null;
    //}

    //private static string BuildAjaxImportMessage(
    //    AkeneoProductBatchImportResult result)
    //{
    //    if (result.Canceled)
    //        return "Akeneo product import was canceled.";

    //    var message =
    //        $"Akeneo product import completed. Read: {result.TotalRead}, Created: {result.CreatedCount}, Updated: {result.UpdatedCount}, Skipped: {result.SkippedCount}, Failed: {result.FailedCount}.";

    //    if (result.Errors.Any() || result.FailedCount > 0)
    //        return "Akeneo product import completed with errors. " + message;

    //    return message;
    //}



    private static string Normalize(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim();
    }
}