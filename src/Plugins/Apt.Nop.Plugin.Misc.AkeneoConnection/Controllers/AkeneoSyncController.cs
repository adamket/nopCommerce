using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Extensions;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Controllers;

[AuthorizeAdmin]
[Area(AreaNames.ADMIN)]
[AutoValidateAntiforgeryToken]
public class AkeneoSyncController(
    AkeneoConnectionSettings akeneoConnectionSettings,
    IAkeneoProductSyncExecutionService productSyncExecutionService,
    IAkeneoProductMappingFactory productMappingFactory,
    IAkeneoProductBatchSyncService productImportService,
    IAkeneoSyncRunRecordService syncRunRecordService, IAkeneoProductBatchImportRequestFactory productBatchImportRequestFactory, IAkeneoSyncProfileService syncProfileService) : BasePluginController
{
    private const string DryRunViewPath =
        "~/Plugins/Apt.Misc.AkeneoConnection/Views/DryRun.cshtml";

    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
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

    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    [HttpPost("admin/akeneo-connection/dry-run")]
    public async Task<IActionResult> DryRun(
        AkeneoProductMappingPreviewModel input,
        CancellationToken cancellationToken)
    {
        var profile = await syncProfileService.GetAkeneoSyncProfileByIdAsync(akeneoConnectionSettings.DefaultSyncProfileId ?? 0);
        if (profile == null && input?.Channel == null)
        {
            return Json(new
            {
                success = false,
                action = "failed",
                errors = new[] { "Please set a default Akeneo Connection sync profile." }
            });
        }

        input ??= new AkeneoProductMappingPreviewModel
        {
            Locale = profile.AkeneoLocales.SplitCsv().FirstOrDefault() ?? "en-US",
            Channel = profile.AkeneoChannel,
            Currency = profile.CurrencyCode
        };

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

    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
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
                errors = new[]
                {
                    "Akeneo product UUID is required. Run the dry run first, then synchronize from the preview result."
                }
            });
        }

        var profileId = akeneoConnectionSettings.DefaultSyncProfileId ?? 0;
        var execution = await productSyncExecutionService.SyncProductByUuidAsync(
            profileId,
            input.Uuid.Trim(),
            cancellationToken);

        if (execution.ProfileNotFound)
        {
            return Json(new
            {
                success = false,
                action = "failed",
                errors = new[] { "Please set a default Akeneo Connection sync profile." }
            });
        }

        return Json(new
        {
            success = execution.Success,
            syncRunRecordId = execution.SyncRunRecordId,
            action = execution.ItemActionType?.ToString() ?? "failed",
            productId = execution.NopProductId,
            akeneoProductUuid = execution.AkeneoProductUuid,
            akeneoIdentifier = execution.AkeneoIdentifier,
            messages = execution.Messages,
            warnings = execution.Warnings,
            errors = execution.Errors
        });
    }

    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    [HttpPost]
    public async Task<IActionResult> ImportProductsByProfile(
        int id,
        bool fullSync,
        CancellationToken cancellationToken)
    {
        var result = await productSyncExecutionService.ImportProductsByProfileAsync(
            id,
            fullSync
                ? SyncType.ManualFullProfileSync
                : SyncType.ManualProfileSync,
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
                warnings = result.WarningCount,
                reconciled = result.ReconciledCount
            },
            warnings = result.Warnings,
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