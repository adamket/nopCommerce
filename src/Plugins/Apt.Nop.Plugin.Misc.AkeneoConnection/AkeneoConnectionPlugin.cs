using Apt.Nop.Plugin.Misc.AkeneoConnection.ScheduleTasks;
using Nop.Core;
using Nop.Core.Domain.Messages;
using Nop.Core.Domain.ScheduleTasks;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Plugins;
using Nop.Services.ScheduleTasks;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection;

public class AkeneoConnectionPlugin(
    ISettingService settingService,
    IWebHelper webHelper,
    IScheduleTaskService scheduleTaskService,
    ILocalizationService localizationService,
    IMessageTemplateService messageTemplateService,
    EmailAccountSettings emailAccountSettings)
    : BasePlugin, IMiscPlugin
{
    public override string GetConfigurationPageUrl() =>
        $"{webHelper.GetStoreLocation()}admin/akeneo-connection/configure";

    public override async Task InstallAsync()
    {
        await settingService.SaveSettingAsync(new AkeneoConnectionSettings());
        await EnsureScheduleTasksAsync();
        await EnsureAlertResourcesAsync();
        await base.InstallAsync();
    }

    public override async Task UpdateAsync(
        string currentVersion,
        string targetVersion)
    {
        // Update migrations handle schema changes. Schedule tasks, message
        // templates and labels are plugin resources, so ensure newly
        // introduced ones exist during an upgrade.
        await EnsureScheduleTasksAsync();
        await EnsureAlertResourcesAsync();
        await base.UpdateAsync(currentVersion, targetVersion);
    }

    public override async Task UninstallAsync()
    {
        await settingService.DeleteSettingAsync<AkeneoConnectionSettings>();

        await DeleteScheduleTaskAsync(
            typeof(AkeneoStaleSyncCheckScheduleTask).FullName);

        foreach (var templateName in new[]
                 {
                     AkeneoConnectionConstants.SyncRunAlertMessageTemplateName,
                     AkeneoConnectionConstants.StaleSyncAlertMessageTemplateName
                 })
        {
            foreach (var template in await messageTemplateService.GetMessageTemplatesByNameAsync(templateName))
                await messageTemplateService.DeleteMessageTemplateAsync(template);
        }

        await localizationService.DeleteLocaleResourcesAsync("Plugins.Misc.AkeneoConnection.SyncAlert");

        await DeleteScheduleTaskAsync(
            typeof(AkeneoProductSyncScheduleTask).FullName);

        await DeleteScheduleTaskAsync(
            typeof(AkeneoProductFullSyncScheduleTask).FullName);

        await DeleteScheduleTaskAsync(
            typeof(AkeneoSyncLogRetentionScheduleTask).FullName);

        await DeleteScheduleTaskAsync(
            typeof(AkeneoCategoryContentSyncScheduleTask).FullName);

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

        // Enabled, but it deletes nothing until a sync profile turns retention on.
        await EnsureScheduleTaskAsync(
            typeof(AkeneoSyncLogRetentionScheduleTask).FullName,
            "Akeneo Connection - Sync Log Retention",
            seconds: 60 * 60 * 24,
            enabled: true);

        // Enabled, but it changes nothing until category content mappings exist.
        await EnsureScheduleTaskAsync(
            typeof(AkeneoCategoryContentSyncScheduleTask).FullName,
            "Akeneo Connection - Category Content Sync",
            seconds: 60 * 60 * 24,
            enabled: true);

        // Enabled, but it sends nothing until alerts are turned on.
        await EnsureScheduleTaskAsync(
            typeof(AkeneoStaleSyncCheckScheduleTask).FullName,
            "Akeneo Connection - Stale Sync Alert",
            seconds: 60 * 60,
            enabled: true);
    }

    /// <summary>
    /// Adds the alert message templates (editable under Content management >
    /// Message templates) and the alert setting labels, keeping any edits.
    /// </summary>
    private async Task EnsureAlertResourcesAsync()
    {
        await EnsureMessageTemplateAsync(
            AkeneoConnectionConstants.SyncRunAlertMessageTemplateName,
            SyncRunAlertSubject,
            SyncRunAlertBody);
        await UpgradeUneditedSyncRunTemplateAsync();

        await EnsureMessageTemplateAsync(
            AkeneoConnectionConstants.StaleSyncAlertMessageTemplateName,
            "%Store.Name%: Akeneo sync has not completed for %Akeneo.Stale.Hours% hours",
            "<p>No scheduled Akeneo sync for profile <strong>%Akeneo.Stale.Profile%</strong> has completed for %Akeneo.Stale.Hours% hours.</p>" +
            "<p>Last completed sync: %Akeneo.Stale.LastCompletedOn%<br />Latest scheduled run: %Akeneo.Stale.LastRun%<br />" +
            "Sync task last started: %Akeneo.Stale.TaskLastStartedOn%</p>" +
            "<p><strong>Latest run errors</strong><br />%Akeneo.Stale.LastRunErrors%</p>" +
            "<p>Check System > Schedule tasks and the sync logs. This alert repeats while no sync completes.</p>" +
            "<p><a href=\"%Akeneo.SyncLogsUrl%\">Open the Akeneo sync logs</a></p>");

        await localizationService.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>
        {
            ["Plugins.Misc.AkeneoConnection.SyncAlertsEnabled"] = "Send sync alerts",
            ["Plugins.Misc.AkeneoConnection.SyncAlertsEnabled.Hint"] = "Email the recipients when a scheduled product or category content sync fails or has failed items, or when no scheduled product sync has completed for a while. Manual runs never send alerts.",
            ["Plugins.Misc.AkeneoConnection.SyncAlertEmails"] = "Alert recipients",
            ["Plugins.Misc.AkeneoConnection.SyncAlertEmails.Hint"] = "Email addresses, separated by commas.",
            ["Plugins.Misc.AkeneoConnection.SyncAlertMinimumFailedProducts"] = "Minimum failed products",
            ["Plugins.Misc.AkeneoConnection.SyncAlertMinimumFailedProducts.Hint"] = "A scheduled run alerts once this many products fail. Runs that fail or are cancelled always alert.",
            ["Plugins.Misc.AkeneoConnection.SyncAlertStaleAfterHours"] = "Stale sync alert after (hours)",
            ["Plugins.Misc.AkeneoConnection.SyncAlertStaleAfterHours.Hint"] = "Alert when no scheduled sync has completed for this many hours. 0 turns the check off."
        });
    }

    // One template serves product and category content runs: %Akeneo.Run.Type% names the sync.
    private const string SyncRunAlertSubject = "%Store.Name%: Akeneo %Akeneo.Run.Type% %Akeneo.Run.Status%";

    private const string SyncRunAlertBody =
        "<p>The scheduled Akeneo %Akeneo.Run.Type% %Akeneo.Run.Status%.</p>" +
        "<p>Run: %Akeneo.Run.Id%<br />Started: %Akeneo.Run.StartedOn%<br />Finished: %Akeneo.Run.FinishedOn%<br />%Akeneo.Run.Counts%</p>" +
        "<p><strong>Run errors</strong><br />%Akeneo.Run.Errors%</p>" +
        "<p><strong>Failed items</strong></p>%Akeneo.Run.FailedItems%" +
        "<p>Failed items were left unchanged in nopCommerce. Warnings are listed in the sync log only.</p>" +
        "<p><a href=\"%Akeneo.SyncLogsUrl%\">Open the Akeneo sync logs</a></p>";

    // The product-only text created before 4.90.30.
    private const string LegacySyncRunAlertSubject = "%Store.Name%: Akeneo sync %Akeneo.Run.Status%";

    private const string LegacySyncRunAlertBody =
        "<p>The scheduled Akeneo sync for profile <strong>%Akeneo.Run.Profile%</strong> %Akeneo.Run.Status%.</p>" +
        "<p>Run: %Akeneo.Run.Id%<br />Started: %Akeneo.Run.StartedOn%<br />Finished: %Akeneo.Run.FinishedOn%<br />%Akeneo.Run.Counts%</p>" +
        "<p><strong>Run errors</strong><br />%Akeneo.Run.Errors%</p>" +
        "<p><strong>Failed products</strong></p>%Akeneo.Run.FailedProducts%" +
        "<p>Failed products were left unchanged in nopCommerce. Warnings are listed in the sync log only.</p>" +
        "<p><a href=\"%Akeneo.SyncLogsUrl%\">Open the Akeneo sync logs</a></p>";

    /// <summary>
    /// Moves the sync run template to the wording that also fits category content runs,
    /// but only while it still holds the original product-only text; edits are kept.
    /// </summary>
    private async Task UpgradeUneditedSyncRunTemplateAsync()
    {
        foreach (var template in await messageTemplateService.GetMessageTemplatesByNameAsync(
                     AkeneoConnectionConstants.SyncRunAlertMessageTemplateName))
        {
            if (template.Subject != LegacySyncRunAlertSubject || template.Body != LegacySyncRunAlertBody)
                continue;

            template.Subject = SyncRunAlertSubject;
            template.Body = SyncRunAlertBody;
            await messageTemplateService.UpdateMessageTemplateAsync(template);
        }
    }

    private async Task EnsureMessageTemplateAsync(string name, string subject, string body)
    {
        if ((await messageTemplateService.GetMessageTemplatesByNameAsync(name)).Any())
            return;

        await messageTemplateService.InsertMessageTemplateAsync(new MessageTemplate
        {
            Name = name,
            Subject = subject,
            Body = body,
            IsActive = true,
            EmailAccountId = emailAccountSettings.DefaultEmailAccountId
        });
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
