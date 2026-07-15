using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Nop.Services.ScheduleTasks;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.ScheduleTasks;

public class AkeneoProductSyncScheduleTask(
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
            SyncType.DeltaSync,
            CancellationToken.None);
    }
}