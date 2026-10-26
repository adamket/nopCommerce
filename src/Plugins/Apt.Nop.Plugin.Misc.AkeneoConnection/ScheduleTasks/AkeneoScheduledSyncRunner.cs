using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.ScheduleTasks;

/// <summary>
/// Runs a scheduled sync and sends its alert. Alerts are sent only from
/// here, so manual runs never email anyone.
/// </summary>
public static class AkeneoScheduledSyncRunner
{
    public static async Task RunAsync(
        Func<Task<AkeneoProductSyncExecutionResult>> runAsync,
        IAkeneoSyncAlertService syncAlertService)
    {
        AkeneoProductSyncExecutionResult result;

        try
        {
            result = await runAsync();
        }
        catch (Exception exception)
        {
            await syncAlertService.NotifyScheduledRunErrorAsync(exception);
            // Rethrow so nopCommerce still records the task as failed.
            throw;
        }

        await syncAlertService.NotifyScheduledRunAsync(result);
    }
}
