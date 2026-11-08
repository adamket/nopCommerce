using System.Linq.Expressions;
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
        await InitializeMissingSettingsAsync();
        await EnsureScheduleTasksAsync();
        await EnsureConfigurationResourcesAsync();
        await EnsureAlertResourcesAsync();
        await RestoreScheduleTasksAsync();
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
        await EnsureConfigurationResourcesAsync();
        await EnsureAlertResourcesAsync();
        await base.UpdateAsync(currentVersion, targetVersion);
    }

    public override async Task UninstallAsync()
    {
        // Preserve settings, task rows and customized templates (including their
        // localized values and store associations) for a later reinstall.
        foreach (var taskType in ScheduleTaskTypes)
            await SuspendScheduleTaskAsync(taskType);

        await localizationService.DeleteLocaleResourcesAsync(ConfigurationResources.Keys.ToList());
        await localizationService.DeleteLocaleResourcesAsync("Plugins.Misc.AkeneoConnection.SyncAlert");

        await base.UninstallAsync();
    }

    private async Task InitializeMissingSettingsAsync()
    {
        var defaults = new AkeneoConnectionSettings();

        // A restored database can already be configured before the plugin is
        // installed. Preserve every existing value, including blanks and false.
        async Task EnsureSettingAsync<T>(Expression<Func<AkeneoConnectionSettings, T>> selector)
        {
            if (!await settingService.SettingExistsAsync(defaults, selector))
                await settingService.SaveSettingAsync(defaults, selector);
        }

        await EnsureSettingAsync(x => x.AkeneoConnectionClientId);
        await EnsureSettingAsync(x => x.AkeneoConnectionClientSecret);
        await EnsureSettingAsync(x => x.AkeneoConnectionBaseUrl);
        await EnsureSettingAsync(x => x.AkeneoConnectionUsername);
        await EnsureSettingAsync(x => x.AkeneoConnectionPassword);
        await EnsureSettingAsync(x => x.DefaultSyncProfileId);
        await EnsureSettingAsync(x => x.SyncAlertsEnabled);
        await EnsureSettingAsync(x => x.SyncAlertEmails);
        await EnsureSettingAsync(x => x.SyncAlertMinimumFailedProducts);
        await EnsureSettingAsync(x => x.SyncAlertStaleAfterHours);
        await EnsureSettingAsync(x => x.SyncAlertLastStaleNotificationUtc);
        await EnsureSettingAsync(x => x.MappingsChangedOnUtc);
        await EnsureSettingAsync(x => x.CategoryContentTranslationLocales);
    }

    private async Task EnsureScheduleTasksAsync()
    {
        await EnsureScheduleTaskAsync(
            typeof(AkeneoProductSyncScheduleTask).FullName,
            "Akeneo Connection - Product Sync",
            seconds: 60 * 60 * 24,
            enabled: true);

        // The full reconciliation task is no longer created. A row left by an earlier
        // version still runs and is still suspended and restored with the plugin.

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

    // Labels for the connection fields on the configure page.
    private static readonly IReadOnlyDictionary<string, string> ConfigurationResources = new Dictionary<string, string>
    {
        ["Plugins.Misc.AkeneoConnection.BaseUrl"] = "Akeneo URL",
        ["Plugins.Misc.AkeneoConnection.BaseUrl.Hint"] = "The base URL of the Akeneo PIM, e.g. https://example.cloud.akeneo.com/.",
        ["Plugins.Misc.AkeneoConnection.ClientId"] = "Client ID",
        ["Plugins.Misc.AkeneoConnection.ClientId.Hint"] = "The client ID of the Akeneo API connection.",
        ["Plugins.Misc.AkeneoConnection.ClientSecret"] = "Client secret",
        ["Plugins.Misc.AkeneoConnection.ClientSecret.Hint"] = "The secret of the Akeneo API connection.",
        ["Plugins.Misc.AkeneoConnection.Username"] = "Username",
        ["Plugins.Misc.AkeneoConnection.Username.Hint"] = "The username of the Akeneo API connection.",
        ["Plugins.Misc.AkeneoConnection.Password"] = "Password",
        ["Plugins.Misc.AkeneoConnection.Password.Hint"] = "The password of the Akeneo API connection.",
        ["Plugins.Misc.AkeneoConnection.DefaultSyncProfile"] = "Default sync profile",
        ["Plugins.Misc.AkeneoConnection.DefaultSyncProfile.Hint"] = "The sync profile used when a sync does not name one."
    };

    private async Task EnsureConfigurationResourcesAsync() =>
        await localizationService.AddOrUpdateLocaleResourceAsync(
            ConfigurationResources.ToDictionary(pair => pair.Key, pair => pair.Value));

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

    private static readonly string[] ScheduleTaskTypes =
    [
        typeof(AkeneoProductSyncScheduleTask).FullName,
        typeof(AkeneoProductFullSyncScheduleTask).FullName,
        typeof(AkeneoSyncLogRetentionScheduleTask).FullName,
        typeof(AkeneoCategoryContentSyncScheduleTask).FullName,
        typeof(AkeneoStaleSyncCheckScheduleTask).FullName
    ];

    private static string SuspendedTaskSettingKey(string taskType) =>
        $"AkeneoConnection.SuspendedTaskEnabled.{taskType}";

    private async Task SuspendScheduleTaskAsync(string taskType)
    {
        var task = await scheduleTaskService.GetTaskByTypeAsync(taskType);
        if (task == null)
            return;

        var key = SuspendedTaskSettingKey(taskType);
        // Save before disabling, and retain the original state if uninstall is retried.
        if (await settingService.GetSettingAsync(key) == null)
            await settingService.SetSettingAsync(key, task.Enabled);

        if (task.Enabled)
        {
            task.Enabled = false;
            await scheduleTaskService.UpdateTaskAsync(task);
        }
    }

    private async Task RestoreScheduleTasksAsync()
    {
        foreach (var taskType in ScheduleTaskTypes)
        {
            var task = await scheduleTaskService.GetTaskByTypeAsync(taskType);
            if (task == null)
                continue;

            var savedState = await settingService.GetSettingAsync(SuspendedTaskSettingKey(taskType));
            if (savedState == null)
                continue;

            task.Enabled = bool.Parse(savedState.Value);
            await scheduleTaskService.UpdateTaskAsync(task);
            // Clear only after restoring successfully, so an interrupted install can retry.
            await settingService.DeleteSettingAsync(savedState);
        }
    }
}
