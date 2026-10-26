using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Nop.Services.ScheduleTasks;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.ScheduleTasks;

/// <summary>
/// Alerts when the scheduled product sync has not finished a run within the
/// configured number of hours. Does nothing until alerts are turned on.
/// </summary>
public class AkeneoStaleSyncCheckScheduleTask(
    IAkeneoSyncAlertService syncAlertService)
    : IScheduleTask
{
    public Task ExecuteAsync() =>
        syncAlertService.CheckForStaleSyncAsync(DateTime.UtcNow);
}
