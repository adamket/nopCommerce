using Apt.Nop.Plugin.Misc.ScheduleTaskRunHistory.ScheduleTasks;
using Nop.Core.Domain.ScheduleTasks;
using Nop.Services.Common;
using Nop.Services.Plugins;
using Nop.Services.ScheduleTasks;

namespace Apt.Nop.Plugin.Misc.ScheduleTaskRunHistory;

public class ScheduleTaskRunHistoryPlugin(IScheduleTaskService scheduleTaskService) : BasePlugin, IMiscPlugin
{

    public override async Task InstallAsync()
    {
        await base.InstallAsync();

        await AddTaskIfNotExistAsync(nameof(ScheduleTaskRunRecordCleanupTask), "Schedule Task History Cleanup", 60 * 60 * 6);
    }

    public async Task AddTaskIfNotExistAsync(string className, string name, int taskSeconds = 60 * 60, string @namespace = "Apt.Nop.Plugin.Misc.ScheduleTaskRunHistory.ScheduleTasks", string assemblyName = "Apt.Nop.Plugin.Misc.ScheduleTaskRunHistory")
    {

        var typeStr = $"{@namespace}.{className}, {assemblyName}";
        var task = await scheduleTaskService.GetTaskByTypeAsync(typeStr);

        if (task == null)
        {
            await scheduleTaskService.InsertTaskAsync(new ScheduleTask
            {
                Type = typeStr,
                Name = name,
                Seconds = taskSeconds
            });
        }
    }



}
