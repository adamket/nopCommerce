using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Nop.Services.ScheduleTasks;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.ScheduleTasks;

/// <summary>
/// Periodic authoritative catalog pass. This intentionally runs separately
/// from the frequent delta task because only a complete full run may reconcile
/// products and variant representations no longer present in profile scope.
/// </summary>
public class AkeneoProductFullSyncScheduleTask(
    IAkeneoProductSyncExecutionService productSyncExecutionService,
    AkeneoConnectionSettings akeneoConnectionSettings)
    : IScheduleTask
{
    public async Task ExecuteAsync()
    {
        if (!akeneoConnectionSettings.DefaultSyncProfileId.HasValue)
            return;

        await productSyncExecutionService.ImportProductsByProfileAsync(
            akeneoConnectionSettings.DefaultSyncProfileId.Value,
            SyncType.ScheduledFullSync,
            CancellationToken.None);
    }
}
