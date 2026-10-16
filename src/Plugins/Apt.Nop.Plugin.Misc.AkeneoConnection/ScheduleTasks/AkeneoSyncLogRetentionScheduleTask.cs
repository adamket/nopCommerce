using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Nop.Services.Logging;
using Nop.Services.ScheduleTasks;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.ScheduleTasks;

/// <summary>
/// Trims old sync run records and item logs for each sync profile that has
/// log retention turned on. Profiles default to off, so the task deletes
/// nothing until a profile opts in.
/// </summary>
public class AkeneoSyncLogRetentionScheduleTask(
    IAkeneoSyncLogRetentionService syncLogRetentionService,
    ILogger logger)
    : IScheduleTask
{
    public async Task ExecuteAsync()
    {
        var result = await syncLogRetentionService.TrimAsync(
            DateTime.UtcNow,
            CancellationToken.None);

        if (result.DeletedItemLogs > 0 || result.DeletedRunRecords > 0)
            await logger.InformationAsync(
                $"Akeneo sync log retention deleted {result.DeletedRunRecords} run record(s) and {result.DeletedItemLogs} item log(s).");
    }
}
