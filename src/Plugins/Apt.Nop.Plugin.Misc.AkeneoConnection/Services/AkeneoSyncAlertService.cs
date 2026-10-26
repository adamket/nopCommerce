using System.Net;
using System.Text;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.ScheduleTasks;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Nop.Core;
using Nop.Core.Domain.Messages;
using Nop.Services.Configuration;
using Nop.Services.Helpers;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Messages;
using Nop.Services.ScheduleTasks;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public class AkeneoSyncAlertService(
    AkeneoConnectionSettings settings,
    EmailAccountSettings emailAccountSettings,
    IAkeneoSyncItemLogService syncItemLogService,
    IAkeneoSyncProfileService syncProfileService,
    IAkeneoSyncRunRecordService syncRunRecordService,
    IDateTimeHelper dateTimeHelper,
    IEmailAccountService emailAccountService,
    ILanguageService languageService,
    ILogger logger,
    IMessageTemplateService messageTemplateService,
    IMessageTokenProvider messageTokenProvider,
    IScheduleTaskService scheduleTaskService,
    ISettingService settingService,
    IStoreContext storeContext,
    IWorkflowMessageService workflowMessageService)
    : IAkeneoSyncAlertService
{
    // Only these run from schedule tasks; every other sync type is started by a person.
    private static readonly SyncType[] ScheduledSyncTypes =
        [SyncType.DeltaSync, SyncType.ScheduledFullSync];

    private const int MaxListedProducts = 50;
    private const string SyncLogsPath = "admin/akeneo-connection/sync-logs/list";

    public async Task NotifyScheduledRunAsync(AkeneoProductSyncExecutionResult result)
    {
        // A run already in progress is not a failure; a stuck one is caught by
        // the stale-sync check.
        if (result == null || result.AlreadyRunning || GetRecipients().Count == 0)
            return;

        try
        {
            var run = result.SyncRunRecordId.HasValue
                ? await syncRunRecordService.GetAkeneoSyncRunRecordByIdAsync(result.SyncRunRecordId.Value)
                : null;

            var runFailed = run == null
                ? !result.Success
                : run.SyncStatusId is (int)SyncStatus.Failed or (int)SyncStatus.Cancelled;
            var failedCount = run?.FailedCount ?? 0;

            if (!runFailed && failedCount < Math.Max(1, settings.SyncAlertMinimumFailedProducts))
                return;

            IList<AkeneoSyncItemLog> failedItems = run == null || failedCount == 0
                ? new List<AkeneoSyncItemLog>()
                : await syncItemLogService.SearchAkeneoSyncItemLogsAsync(
                    syncRunRecordId: run.Id,
                    actionTypeId: (int)SyncItemActionType.Failed,
                    pageSize: MaxListedProducts);

            var status = run != null
                ? DescribeStatus((SyncStatus)run.SyncStatusId)
                : "could not start";

            var tokens = new List<Token>
            {
                new("Akeneo.Run.Id", run?.Id.ToString() ?? "none"),
                new("Akeneo.Run.Status", status),
                new("Akeneo.Run.Profile", await GetProfileNameAsync(result.ProfileId)),
                new("Akeneo.Run.StartedOn", await FormatDateAsync(run?.StartedOnUtc)),
                new("Akeneo.Run.FinishedOn", await FormatDateAsync(run?.FinishedOnUtc)),
                new("Akeneo.Run.Counts", run == null
                    ? "No products were read."
                    : $"Read {run.TotalRead}, created {run.CreatedCount}, updated {run.UpdatedCount}, " +
                      $"skipped {run.SkippedCount}, failed {run.FailedCount}."),
                // Product errors are in the table; this is for the run itself.
                new("Akeneo.Run.Errors", runFailed
                    ? FormatMultiline(run?.ErrorSummary ?? result.Message)
                    : "-", true),
                new("Akeneo.Run.FailedProducts", FormatFailedProducts(failedItems, failedCount), true)
            };

            await SendAsync(AkeneoConnectionConstants.SyncRunAlertMessageTemplateName, tokens);
        }
        catch (Exception exception)
        {
            await logger.ErrorAsync("Akeneo sync alert could not be sent.", exception);
        }
    }

    public async Task NotifyScheduledRunErrorAsync(Exception exception)
    {
        if (exception == null || GetRecipients().Count == 0)
            return;

        await NotifyScheduledRunAsync(new AkeneoProductSyncExecutionResult
        {
            Success = false,
            ProfileId = settings.DefaultSyncProfileId,
            SyncStatus = SyncStatus.Failed,
            Message = exception.Message
        });
    }

    public async Task CheckForStaleSyncAsync(DateTime utcNow)
    {
        if (settings.SyncAlertStaleAfterHours <= 0 ||
            !settings.DefaultSyncProfileId.HasValue ||
            GetRecipients().Count == 0)
        {
            return;
        }

        try
        {
            // A schedule someone turned off is not stale.
            var task = await scheduleTaskService.GetTaskByTypeAsync(
                typeof(AkeneoProductSyncScheduleTask).FullName);
            if (task is not { Enabled: true })
                return;

            var profileId = settings.DefaultSyncProfileId.Value;
            var threshold = TimeSpan.FromHours(settings.SyncAlertStaleAfterHours);
            var lastCompleted = await syncRunRecordService.GetLastFinishedRunAsync(
                profileId, ScheduledSyncTypes, onlyCompleted: true);

            // Without a completed run, measure from when the schedule was turned on.
            var since = lastCompleted?.FinishedOnUtc ?? task.LastEnabledUtc;
            if (!since.HasValue || utcNow - since.Value < threshold)
                return;

            // Repeat once per threshold while the same stale period lasts.
            var lastAlert = settings.SyncAlertLastStaleNotificationUtc;
            if (lastAlert.HasValue && lastAlert.Value > since.Value && utcNow - lastAlert.Value < threshold)
                return;

            var lastRun = await syncRunRecordService.GetLastFinishedRunAsync(
                profileId, ScheduledSyncTypes, onlyCompleted: false);

            var tokens = new List<Token>
            {
                new("Akeneo.Stale.Profile", await GetProfileNameAsync(profileId)),
                new("Akeneo.Stale.Hours", ((int)(utcNow - since.Value).TotalHours).ToString()),
                new("Akeneo.Stale.LastCompletedOn", lastCompleted == null
                    ? "never"
                    : await FormatDateAsync(lastCompleted.FinishedOnUtc)),
                new("Akeneo.Stale.LastRun", lastRun == null
                    ? "No scheduled run has finished."
                    : $"Run {lastRun.Id} {DescribeStatus((SyncStatus)lastRun.SyncStatusId)} " +
                      $"on {await FormatDateAsync(lastRun.FinishedOnUtc)}."),
                new("Akeneo.Stale.LastRunErrors", FormatMultiline(lastRun?.ErrorSummary), true),
                new("Akeneo.Stale.TaskLastStartedOn", await FormatDateAsync(task.LastStartUtc))
            };

            if (await SendAsync(AkeneoConnectionConstants.StaleSyncAlertMessageTemplateName, tokens))
            {
                settings.SyncAlertLastStaleNotificationUtc = utcNow;
                await settingService.SaveSettingAsync(
                    settings,
                    setting => setting.SyncAlertLastStaleNotificationUtc,
                    clearCache: true);
            }
        }
        catch (Exception exception)
        {
            await logger.ErrorAsync("Akeneo stale-sync alert could not be sent.", exception);
        }
    }

    /// <summary>
    /// Queues the template for every recipient. Returns false when the
    /// template is missing or inactive, or no email account exists.
    /// </summary>
    private async Task<bool> SendAsync(string templateName, IList<Token> tokens)
    {
        var store = await storeContext.GetCurrentStoreAsync();
        var template = (await messageTemplateService.GetMessageTemplatesByNameAsync(templateName, store.Id))
            .FirstOrDefault();

        if (template is not { IsActive: true })
            return false;

        var emailAccount = await emailAccountService.GetEmailAccountByIdAsync(template.EmailAccountId) ??
                           await emailAccountService.GetEmailAccountByIdAsync(emailAccountSettings.DefaultEmailAccountId) ??
                           (await emailAccountService.GetAllEmailAccountsAsync()).FirstOrDefault();

        if (emailAccount == null)
        {
            await logger.WarningAsync($"Akeneo alert '{templateName}' was not sent: no email account is configured.");
            return false;
        }

        var languageId = (await languageService.GetAllLanguagesAsync(storeId: store.Id)).FirstOrDefault()?.Id ?? 0;

        await messageTokenProvider.AddStoreTokensAsync(tokens, store, emailAccount, languageId);
        tokens.Add(new Token("Akeneo.SyncLogsUrl", $"{store.Url?.TrimEnd('/')}/{SyncLogsPath}", true));

        foreach (var recipient in GetRecipients())
        {
            await workflowMessageService.SendNotificationAsync(
                template, emailAccount, languageId, tokens, recipient, null);
        }

        return true;
    }

    private IList<string> GetRecipients()
    {
        if (!settings.SyncAlertsEnabled || string.IsNullOrWhiteSpace(settings.SyncAlertEmails))
            return new List<string>();

        return ParseRecipients(settings.SyncAlertEmails);
    }

    public static IList<string> ParseRecipients(string value) =>
        (value ?? string.Empty)
            .Split([',', ';', '\r', '\n', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Where(CommonHelper.IsValidEmail)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private async Task<string> GetProfileNameAsync(int? profileId)
    {
        if (!profileId.HasValue)
            return "none";

        var profile = await syncProfileService.GetAkeneoSyncProfileByIdAsync(profileId.Value);
        return profile?.Name ?? $"profile {profileId.Value}";
    }

    private async Task<string> FormatDateAsync(DateTime? utc) =>
        utc.HasValue
            ? (await dateTimeHelper.ConvertToUserTimeAsync(utc.Value, DateTimeKind.Utc)).ToString("yyyy-MM-dd HH:mm")
            : "-";

    private static string DescribeStatus(SyncStatus status) => status switch
    {
        SyncStatus.Started => "is still running",
        SyncStatus.Completed => "completed",
        SyncStatus.CompletedWithWarnings => "completed with warnings",
        SyncStatus.CompletedWithErrors => "completed with failed products",
        SyncStatus.Failed => "failed",
        SyncStatus.Cancelled => "was cancelled",
        _ => status.ToString()
    };

    private static string FormatMultiline(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? "-"
            : WebUtility.HtmlEncode(value.Trim()).Replace("\r\n", "\n").Replace("\n", "<br />");

    /// <summary>
    /// An HTML table of failed products, using the errors from each item log.
    /// </summary>
    public static string FormatFailedProducts(IList<AkeneoSyncItemLog> items, int failedCount)
    {
        if (items.Count == 0)
            return "<p>No failed products.</p>";

        var html = new StringBuilder();
        html.Append("<table border=\"1\" cellpadding=\"4\" cellspacing=\"0\" style=\"border-collapse:collapse\">");
        html.Append("<tr><th align=\"left\">Akeneo identifier</th><th align=\"left\">nop product ID</th><th align=\"left\">Error</th></tr>");

        foreach (var item in items)
        {
            html.Append("<tr><td>")
                .Append(WebUtility.HtmlEncode(item.AkeneoIdentifier ?? item.AkeneoProductUuid ?? "-"))
                .Append("</td><td>")
                .Append(item.NopProductId > 0 ? item.NopProductId.ToString() : "-")
                .Append("</td><td>")
                .Append(WebUtility.HtmlEncode(ExtractErrors(item.Message)))
                .Append("</td></tr>");
        }

        html.Append("</table>");

        if (failedCount > items.Count)
            html.Append($"<p>&hellip;and {failedCount - items.Count} more. See the sync log for the full list.</p>");

        return html.ToString();
    }

    /// <summary>
    /// Item log messages hold "Messages:", "Warnings:" and "Errors:" lines;
    /// the alert lists only the errors.
    /// </summary>
    private static string ExtractErrors(string message)
    {
        const string prefix = "Errors: ";

        var errors = (message ?? string.Empty)
            .Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal));

        var text = errors != null ? errors[prefix.Length..] : message ?? string.Empty;

        return text.Length > 500 ? text[..500] + "…" : text;
    }
}
