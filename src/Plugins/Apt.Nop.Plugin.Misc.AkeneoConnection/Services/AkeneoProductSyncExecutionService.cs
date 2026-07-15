using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Humanizer;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public class AkeneoProductSyncExecutionService(
    IAkeneoProductBatchSyncService productImportService,
    IAkeneoSyncRunRecordService syncRunRecordService,
    IAkeneoProductBatchImportRequestFactory productBatchImportRequestFactory,
    IAkeneoSyncProfileService syncProfileService)
    : IAkeneoProductSyncExecutionService
{
    public async Task<AkeneoProductSyncExecutionResult> ImportProductsByProfileAsync(
        int profileId,
        SyncType syncType,
        CancellationToken cancellationToken = default)
    {
        var activeRun = await GetActiveSyncRunAsync();

        if (activeRun != null)
            return BuildAlreadyRunningResult(profileId, activeRun.Id);

        var profile = await syncProfileService.GetAkeneoSyncProfileByIdAsync(profileId);

        if (profile == null)
        {
            return new AkeneoProductSyncExecutionResult
            {
                Success = false,
                ProfileNotFound = true,
                ProfileId = profileId,
                SyncStatus = SyncStatus.Failed,
                Message = "Akeneo sync profile was not found."
            };
        }

        if (!profile.Enabled)
        {
            return new AkeneoProductSyncExecutionResult
            {
                Success = false,
                ProfileDisabled = true,
                ProfileId = profile.Id,
                SyncStatus = SyncStatus.Failed,
                Message = "This Akeneo sync profile is disabled."
            };
        }

        DateTime? lastSuccessfulRunStartedOnUtc = null;

        if ((AkeneoUpdatedFilterMode)profile.UpdatedFilterModeId ==
            AkeneoUpdatedFilterMode.SinceLastSuccessfulRun)
        {
            lastSuccessfulRunStartedOnUtc = await GetLastSuccessfulRunStartedOnUtcAsync();
        }

        var runRecord = new AkeneoSyncRunRecord
        {
            SyncTypeId = (int)syncType,
            StartedOnUtc = DateTime.UtcNow,
            SyncStatusId = (int)SyncStatus.Started,
            ErrorSummary = null
        };

        await syncRunRecordService.InsertAkeneoSyncRunRecordAsync(runRecord);

        var request = productBatchImportRequestFactory.CreateFromProfile(
            profile,
            runRecord.Id,
            lastSuccessfulRunStartedOnUtc);

        return await ImportProductsCoreAsync(request, runRecord, profile.Id, cancellationToken);
    }

    public async Task<AkeneoProductSyncExecutionResult> ImportProductsAsync(
        AkeneoProductBatchImportRequest request,
        SyncType syncType,
        int? profileId = null,
        CancellationToken cancellationToken = default)
    {
        var activeRun = await GetActiveSyncRunAsync();

        if (activeRun != null)
            return BuildAlreadyRunningResult(profileId, activeRun.Id);
        var runRecord = new AkeneoSyncRunRecord
        {
            SyncTypeId = (int)syncType,
            StartedOnUtc = DateTime.UtcNow,
            SyncStatusId = (int)SyncStatus.Started,
            ErrorSummary = null
        };

        await syncRunRecordService.InsertAkeneoSyncRunRecordAsync(runRecord);

        request.SyncRunRecordId = runRecord.Id;

        return await ImportProductsCoreAsync(
            request,
            runRecord,
            profileId,
            cancellationToken);
    }

    private async Task<AkeneoProductSyncExecutionResult> ImportProductsCoreAsync(
        AkeneoProductBatchImportRequest request,
        AkeneoSyncRunRecord runRecord,
        int? profileId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await productImportService.SyncProductsAsync(
                request,
                cancellationToken);

            ApplyImportResultToRunRecord(runRecord, result);

            await syncRunRecordService.UpdateAkeneoSyncRunRecordAsync(runRecord);

            return BuildExecutionResult(runRecord, result, profileId);
        }
        catch (OperationCanceledException)
        {
            runRecord.FinishedOnUtc = DateTime.UtcNow;
            runRecord.SyncStatusId = (int)SyncStatus.Cancelled;
            runRecord.ErrorSummary = "Product import was canceled.";

            await syncRunRecordService.UpdateAkeneoSyncRunRecordAsync(runRecord);

            return new AkeneoProductSyncExecutionResult
            {
                Success = false,
                Canceled = true,
                ProfileId = profileId,
                SyncRunRecordId = runRecord.Id,
                SyncStatus = SyncStatus.Cancelled,
                Message = "Akeneo product import was canceled."
            };
        }
        catch (Exception ex)
        {
            runRecord.FinishedOnUtc = DateTime.UtcNow;
            runRecord.SyncStatusId = (int)SyncStatus.Failed;
            runRecord.ErrorSummary = ex.Message.Truncate(4000);

            await syncRunRecordService.UpdateAkeneoSyncRunRecordAsync(runRecord);

            return new AkeneoProductSyncExecutionResult
            {
                Success = false,
                ProfileId = profileId,
                SyncRunRecordId = runRecord.Id,
                SyncStatus = SyncStatus.Failed,
                Message = $"Akeneo product import failed: {ex.Message}",
                Errors = new List<string> { ex.Message }
            };
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

    private static AkeneoProductSyncExecutionResult BuildExecutionResult(
        AkeneoSyncRunRecord runRecord,
        AkeneoProductBatchImportResult result,
        int? profileId)
    {
        var completedWithErrors = result.Errors.Any() || result.FailedCount > 0;
        var syncStatus = (SyncStatus)runRecord.SyncStatusId;

        return new AkeneoProductSyncExecutionResult
        {
            Success = !result.Canceled && !completedWithErrors,
            Canceled = result.Canceled,
            CompletedWithErrors = completedWithErrors,
            ProfileId = profileId,
            SyncRunRecordId = runRecord.Id,
            SyncStatus = syncStatus,
            Message = BuildImportMessage(result),
            TotalRead = result.TotalRead,
            CreatedCount = result.CreatedCount,
            UpdatedCount = result.UpdatedCount,
            SkippedCount = result.SkippedCount,
            FailedCount = result.FailedCount,
            WarningCount = result.WarningCount,
            Errors = result.Errors,
            Messages = result.Messages
        };
    }

    private static string BuildImportMessage(
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

    private async Task<AkeneoSyncRunRecord> GetActiveSyncRunAsync()
    {
        var windowStartUtc = DateTime.UtcNow.AddMinutes(-120);

        var candidates = await syncRunRecordService.SearchAkeneoSyncRunRecordsAsync(
            createdFromUtc: windowStartUtc,
            syncStatusId: (int)SyncStatus.Started,
            pageSize: 1);

        return candidates.FirstOrDefault(record => record.FinishedOnUtc == null);
    }

    private async Task<DateTime?> GetLastSuccessfulRunStartedOnUtcAsync()
    {
        var recent = await syncRunRecordService.SearchAkeneoSyncRunRecordsAsync(
            pageIndex: 0,
            pageSize: 50);

        return recent.FirstOrDefault(record =>
                record.SyncStatusId == (int)SyncStatus.Completed ||
                record.SyncStatusId == (int)SyncStatus.CompletedWithWarnings)
            ?.StartedOnUtc;
    }

    private static AkeneoProductSyncExecutionResult BuildAlreadyRunningResult(
        int? profileId,
        int activeRunRecordId)
    {
        return new AkeneoProductSyncExecutionResult
        {
            Success = false,
            AlreadyRunning = true,
            ProfileId = profileId,
            SyncRunRecordId = activeRunRecordId,
            SyncStatus = SyncStatus.Failed,
            Message = $"An Akeneo sync is already running (run record {activeRunRecordId}). Skipped."
        };
    }
}