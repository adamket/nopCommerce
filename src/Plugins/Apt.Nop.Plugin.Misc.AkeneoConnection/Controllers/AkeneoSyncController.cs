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


    //[HttpPost]
    //public async Task<IActionResult> ImportAllProducts(
    //    CancellationToken cancellationToken)
    //{
    //    var syncRun = new AkeneoSyncRunRecord
    //    {
    //        SyncTypeId = (int)SyncType.ManualProductSync,
    //        StartedOnUtc = DateTime.UtcNow,
    //        SyncStatusId = (int)SyncStatus.Started
    //    };

    //    await syncRunRecordService.InsertAkeneoSyncRunRecordAsync(syncRun);


    //    var result = await akeneoProductImportService.ImportProductsAsync(
    //        new AkeneoProductBatchImportRequest
    //        {
    //            SyncRunRecordId = syncRun.Id,
    //            PageSize = 100,
    //            CreateNewProducts = true,
    //            UpdateExistingProducts = true,
    //            AddMappedCategories = true,
    //            AddMappedManufacturers = true,
    //            CreateMissingSpecificationAttributeOptions = true,
    //            CreateMissingProductAttributeValues = true,
    //            SaveRawPayloadSnapshot = false
    //        },
    //        cancellationToken);

    //    syncRun.FinishedOnUtc = DateTime.UtcNow;
    //    syncRun.TotalRead = result.TotalRead;
    //    syncRun.CreatedCount = result.CreatedCount;
    //    syncRun.UpdatedCount = result.UpdatedCount;
    //    syncRun.SkippedCount = result.SkippedCount;
    //    syncRun.FailedCount = result.FailedCount;
    //    syncRun.SyncStatusId = (int)result.SyncStatus;
    //    syncRun.ErrorSummary = result.Errors.Any()
    //        ? string.Join(" | ", result.Errors)
    //        : null;

    //    await syncRunRecordService.UpdateAkeneoSyncRunRecordAsync(syncRun);

    //    return Json(new
    //    {
    //        success = result.Success,
    //        result.TotalRead,
    //        result.CreatedCount,
    //        result.UpdatedCount,
    //        result.SkippedCount,
    //        result.FailedCount,
    //        result.WarningCount,
    //        result.Canceled,
    //        result.Messages,
    //        result.Errors,
    //        result.SkippedSkuSample
    //    });
    //}

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
        var profile = await syncProfileService.GetAkeneoSyncProfileByIdAsync(id);

        if (profile == null)
        {
            return NotFound(new
            {
                success = false,
                message = "Akeneo sync profile was not found."
            });
        }

        if (!profile.Enabled)
        {
            return BadRequest(new
            {
                success = false,
                message = "This Akeneo sync profile is disabled.",
                profileId = profile.Id,
                //editUrl = Url.Action(nameof(Edit), new { id = profile.Id })
            });
        }

        var runRecord = new AkeneoSyncRunRecord
        {
            SyncTypeId = (int)SyncType.ManualProductSync,
            StartedOnUtc = DateTime.UtcNow,
            SyncStatusId = (int)SyncStatus.Started,
            ErrorSummary = null
        };

        await syncRunRecordService.InsertAkeneoSyncRunRecordAsync(runRecord);

        try
        {
            var request = productBatchImportRequestFactory.CreateFromProfile(
                profile,
                runRecord.Id);

            var result = await productImportService.ImportProductsAsync(
                request,
                cancellationToken);

            ApplyImportResultToRunRecord(runRecord, result);

            await syncRunRecordService.UpdateAkeneoSyncRunRecordAsync(runRecord);

            var completedWithErrors =
                result.Errors.Any() ||
                result.FailedCount > 0;

            return Json(new
            {
                success = !result.Canceled && !completedWithErrors,
                canceled = result.Canceled,
                completedWithErrors,
                message = BuildAjaxImportMessage(result),
                profileId = profile.Id,
                syncRunRecordId = runRecord.Id,
                syncStatusId = runRecord.SyncStatusId,
                syncStatus = ((SyncStatus)runRecord.SyncStatusId).ToString(),
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
                messages = result.Messages,
                //editUrl = Url.Action(nameof(Edit), new { id = profile.Id })
            });
        }
        catch (OperationCanceledException)
        {
            runRecord.FinishedOnUtc = DateTime.UtcNow;
            runRecord.SyncStatusId = (int)SyncStatus.Cancelled;
            runRecord.ErrorSummary = "Product import was canceled.";

            await syncRunRecordService.UpdateAkeneoSyncRunRecordAsync(runRecord);

            return Json(new
            {
                success = false,
                canceled = true,
                message = "Akeneo product import was canceled.",
                profileId = profile.Id,
                syncRunRecordId = runRecord.Id,
                syncStatusId = runRecord.SyncStatusId,
                syncStatus = ((SyncStatus)runRecord.SyncStatusId).ToString(),
                //editUrl = Url.Action(nameof(Edit), new { id = profile.Id })
            });
        }
        catch (Exception ex)
        {
            runRecord.FinishedOnUtc = DateTime.UtcNow;
            runRecord.SyncStatusId = (int)SyncStatus.Failed;
            runRecord.ErrorSummary = ex.Message.Truncate(4000);

            await syncRunRecordService.UpdateAkeneoSyncRunRecordAsync(runRecord);

            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                message = $"Akeneo product import failed: {ex.Message}",
                profileId = profile.Id,
                syncRunRecordId = runRecord.Id,
                syncStatusId = runRecord.SyncStatusId,
                syncStatus = ((SyncStatus)runRecord.SyncStatusId).ToString(),
                error = ex.Message,
                //editUrl = Url.Action(nameof(Edit), new { id = profile.Id })
            });
        }
    }


    private static void ApplyImportResultToRunRecord(
        AkeneoSyncRunRecord runRecord,
        AkeneoProductBatchImportResult result)
    {
        runRecord.FinishedOnUtc = DateTime.UtcNow;
        runRecord.TotalRead = result.TotalRead;
        runRecord.CreatedCount = result.CreatedCount;
        runRecord.UpdatedCount = result.UpdatedCount;
        runRecord.SkippedCount = result.SkippedCount;
        runRecord.FailedCount = result.FailedCount;

        if (result.Canceled)
        {
            runRecord.SyncStatusId = (int)SyncStatus.Cancelled;
            runRecord.ErrorSummary = "Product import was canceled.";
            return;
        }

        if (result.Errors.Any() || result.FailedCount > 0)
        {
            runRecord.SyncStatusId = (int)SyncStatus.CompletedWithErrors;
            runRecord.ErrorSummary = string.Join(Environment.NewLine, result.Errors).Truncate(4000);
            return;
        }

        runRecord.SyncStatusId = (int)SyncStatus.Completed;
        runRecord.ErrorSummary = null;
    }

    private static string BuildAjaxImportMessage(
        AkeneoProductBatchImportResult result)
    {
        if (result.Canceled)
            return "Akeneo product import was canceled.";

        var message =
            $"Akeneo product import completed. Read: {result.TotalRead}, Created: {result.CreatedCount}, Updated: {result.UpdatedCount}, Skipped: {result.SkippedCount}, Failed: {result.FailedCount}.";

        if (result.Errors.Any() || result.FailedCount > 0)
            return "Akeneo product import completed with errors. " + message;

        return message;
    }



    private static string Normalize(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim();
    }
}