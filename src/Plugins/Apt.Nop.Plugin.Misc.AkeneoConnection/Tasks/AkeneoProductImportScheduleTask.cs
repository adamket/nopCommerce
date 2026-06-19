using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Nop.Services.ScheduleTasks;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tasks;
public class AkeneoProductImportScheduleTask(
    IAkeneoProductImportService akeneoProductImportService,
    IAkeneoSyncRunRecordService syncRunRecordService)
    : IScheduleTask
{
    public async Task ExecuteAsync()
    {
        var syncRun = new AkeneoSyncRunRecord
        {
            SyncTypeId = (int)SyncType.DeltaSync,
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
            });

        syncRun.FinishedOnUtc = DateTime.UtcNow;
        syncRun.TotalRead = result.TotalRead;
        syncRun.CreatedCount = result.CreatedCount;
        syncRun.UpdatedCount = result.UpdatedCount;
        syncRun.SkippedCount = result.SkippedCount;
        syncRun.FailedCount = result.FailedCount;
        syncRun.Status = result.Success ? "Completed" : "CompletedWithErrors";
        syncRun.ErrorSummary = result.Errors.Any()
            ? string.Join(" | ", result.Errors)
            : null;

        await syncRunRecordService.UpdateAkeneoSyncRunRecordAsync(syncRun);
    }
}