using Apt.Nop.Plugin.Misc.AkeneoConnection.ScheduleTasks;
using Nop.Core;
using Nop.Core.Domain.ScheduleTasks;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Plugins;
using Nop.Services.ScheduleTasks;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection;

public class AkeneoConnectionPlugin(
    ISettingService settingService,
    IWebHelper webHelper,
    IScheduleTaskService scheduleTaskService)
    : BasePlugin, IMiscPlugin
{
    public override string GetConfigurationPageUrl() =>
        $"{webHelper.GetStoreLocation()}admin/akeneo-connection/configure";

    public override async Task InstallAsync()
    {
        await settingService.SaveSettingAsync(new AkeneoConnectionSettings());
        await EnsureScheduleTasksAsync();
        await base.InstallAsync();
    }

    public override async Task UpdateAsync(
        string currentVersion,
        string targetVersion)
    {
        // Update migrations handle schema changes. Schedule tasks are plugin
        // resources, so ensure newly introduced tasks exist during an upgrade.
        await EnsureScheduleTasksAsync();
        await base.UpdateAsync(currentVersion, targetVersion);
    }

    public override async Task UninstallAsync()
    {
        await settingService.DeleteSettingAsync<AkeneoConnectionSettings>();

        await DeleteScheduleTaskAsync(
            typeof(AkeneoProductSyncScheduleTask).FullName);

        await DeleteScheduleTaskAsync(
            typeof(AkeneoProductFullSyncScheduleTask).FullName);

        await base.UninstallAsync();
    }

    private async Task EnsureScheduleTasksAsync()
    {
        await EnsureScheduleTaskAsync(
            typeof(AkeneoProductSyncScheduleTask).FullName,
            "Akeneo Connection - Product Sync",
            seconds: 60 * 60 * 24,
            enabled: true);

        await EnsureScheduleTaskAsync(
            typeof(AkeneoProductFullSyncScheduleTask).FullName,
            "Akeneo Connection - Full Product Reconciliation",
            seconds: 60 * 60 * 24 * 7,
            enabled: false);
    }

    private async Task EnsureScheduleTaskAsync(
        string taskType,
        string name,
        int seconds,
        bool enabled)
    {
        if (string.IsNullOrWhiteSpace(taskType))
            throw new InvalidOperationException(
                $"Unable to resolve schedule task type for '{name}'.");

        if (await scheduleTaskService.GetTaskByTypeAsync(taskType) != null)
            return;

        await scheduleTaskService.InsertTaskAsync(new ScheduleTask
        {
            Enabled = enabled,
            Seconds = seconds,
            Type = taskType,
            Name = name
        });
    }

    private async Task DeleteScheduleTaskAsync(string taskType)
    {
        if (string.IsNullOrWhiteSpace(taskType))
            return;

        var task = await scheduleTaskService.GetTaskByTypeAsync(taskType);
        if (task != null)
            await scheduleTaskService.DeleteTaskAsync(task);
    }
}
