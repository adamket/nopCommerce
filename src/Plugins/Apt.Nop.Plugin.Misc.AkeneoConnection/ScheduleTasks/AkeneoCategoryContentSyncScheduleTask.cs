using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Categories;
using Nop.Services.Logging;
using Nop.Services.ScheduleTasks;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.ScheduleTasks;

/// <summary>
/// Copies mapped Akeneo category content into nopCommerce. It does nothing
/// until category content mappings exist, and skips a run while another
/// catalog writer holds the lease. Failed runs and failed categories are
/// emailed from here, so manual category syncs never send alerts.
/// </summary>
public class AkeneoCategoryContentSyncScheduleTask(
    IAkeneoCategoryContentSyncService categoryContentSyncService,
    IAkeneoSyncAlertService syncAlertService,
    ILogger logger)
    : IScheduleTask
{
    public async Task ExecuteAsync()
    {
        AkeneoCategoryContentRunResult result;

        try
        {
            result = await categoryContentSyncService.SyncAsync(cancellationToken: CancellationToken.None);
        }
        catch (Exception exception)
        {
            await syncAlertService.NotifyScheduledCategoryContentRunAsync(new AkeneoCategoryContentRunResult
            {
                Success = false,
                Message = $"Category content sync failed: {exception.Message}"
            });
            // Rethrow so nopCommerce still records the task as failed.
            throw;
        }

        await syncAlertService.NotifyScheduledCategoryContentRunAsync(result);

        if (result.AlreadyRunning)
        {
            await logger.InformationAsync($"Akeneo category content sync skipped: {result.Message}");
            return;
        }

        if (!result.Success)
            await logger.WarningAsync($"Akeneo category content sync: {result.Message} See the Akeneo sync logs for details.");
    }
}
