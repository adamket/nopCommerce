using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Nop.Services.ScheduleTasks;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tasks;

public class AkeneoProductImportScheduleTask(
    IAkeneoProductImportExecutionService productImportExecutionService,
    AkeneoConnectionSettings akeneoConnectionSettings)
    : IScheduleTask
{
    public async Task ExecuteAsync()
    {
        if (!akeneoConnectionSettings.DefaultSyncProfileId.HasValue)
            return;

        await productImportExecutionService.ImportProductsByProfileAsync(
            akeneoConnectionSettings.DefaultSyncProfileId.Value,
            SyncType.DeltaSync,
            CancellationToken.None);
    }
}