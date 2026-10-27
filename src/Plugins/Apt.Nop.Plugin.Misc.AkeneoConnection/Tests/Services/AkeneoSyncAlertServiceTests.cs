using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.ScheduleTasks;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Categories;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Nop.Core;
using Nop.Core.Domain.Localization;
using Nop.Core.Domain.Messages;
using Nop.Core.Domain.ScheduleTasks;
using Nop.Core.Domain.Stores;
using Nop.Services.Configuration;
using Nop.Services.Helpers;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Messages;
using Nop.Services.ScheduleTasks;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

[TestFixture]
public class AkeneoSyncAlertServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);

    private AkeneoConnectionSettings _settings = null!;
    private Mock<IAkeneoSyncItemLogService> _itemLogs = null!;
    private Mock<IAkeneoSyncRunRecordService> _runs = null!;
    private Mock<IScheduleTaskService> _scheduleTasks = null!;
    private Mock<ISettingService> _settingService = null!;
    private Mock<IWorkflowMessageService> _workflow = null!;
    private List<IList<Token>> _sentTokens = null!;
    private List<string> _sentTo = null!;
    private ScheduleTask _syncTask = null!;

    [SetUp]
    public void SetUp()
    {
        _settings = new AkeneoConnectionSettings
        {
            DefaultSyncProfileId = 3,
            SyncAlertsEnabled = true,
            SyncAlertEmails = "ops@example.com; merch@example.com"
        };
        _itemLogs = new Mock<IAkeneoSyncItemLogService>();
        _runs = new Mock<IAkeneoSyncRunRecordService>();
        _scheduleTasks = new Mock<IScheduleTaskService>();
        _settingService = new Mock<ISettingService>();
        _workflow = new Mock<IWorkflowMessageService>();
        _sentTokens = new List<IList<Token>>();
        _sentTo = new List<string>();
        _syncTask = new ScheduleTask { Enabled = true, LastEnabledUtc = Now.AddDays(-10) };

        _scheduleTasks
            .Setup(service => service.GetTaskByTypeAsync(typeof(AkeneoProductSyncScheduleTask).FullName))
            .ReturnsAsync(() => _syncTask);
        _workflow
            .Setup(service => service.SendNotificationAsync(
                It.IsAny<MessageTemplate>(), It.IsAny<EmailAccount>(), It.IsAny<int>(), It.IsAny<IList<Token>>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
            .Callback((MessageTemplate _, EmailAccount _, int _, IList<Token> tokens, string to, string _,
                string _, string _, string _, string _, string _, string _, string _, bool _) =>
            {
                _sentTokens.Add(tokens);
                _sentTo.Add(to);
            })
            .ReturnsAsync(1);
    }

    [Test]
    public async Task Run_with_failed_products_emails_every_recipient_with_the_failures()
    {
        Run(new AkeneoSyncRunRecord { Id = 9, SyncStatusId = (int)SyncStatus.CompletedWithErrors, FailedCount = 1 });
        _itemLogs
            .Setup(service => service.SearchAkeneoSyncItemLogsAsync(9, null, null, null, (int)SyncItemActionType.Failed, 0, 50))
            .ReturnsAsync(new PagedList<AkeneoSyncItemLog>(new List<AkeneoSyncItemLog>
            {
                new()
                {
                    AkeneoIdentifier = "RED-MAPLE",
                    Message = "Messages: x\nErrors: The Akeneo URL slug 'red-maple' is already used by product 'Red Maple' (ID 5)"
                }
            }, 0, 50));

        await Service().NotifyScheduledRunAsync(Result(9));

        Assert.Multiple(() =>
        {
            Assert.That(_sentTo, Is.EqualTo(new[] { "ops@example.com", "merch@example.com" }));
            Assert.That(Token("Akeneo.Run.Status"), Is.EqualTo("completed with failed products"));
            Assert.That(Token("Akeneo.Run.FailedProducts"), Does.Contain("RED-MAPLE")
                .And.Contain("already used by product &#39;Red Maple&#39; (ID 5)")
                .And.Not.Contain("Messages:"));
        });
    }

    [Test]
    public async Task Run_below_the_failed_product_minimum_sends_nothing()
    {
        _settings.SyncAlertMinimumFailedProducts = 3;
        Run(new AkeneoSyncRunRecord { Id = 9, SyncStatusId = (int)SyncStatus.CompletedWithErrors, FailedCount = 2 });

        await Service().NotifyScheduledRunAsync(Result(9));

        Assert.That(_sentTo, Is.Empty);
    }

    [Test]
    public async Task Run_with_only_warnings_sends_nothing()
    {
        Run(new AkeneoSyncRunRecord { Id = 9, SyncStatusId = (int)SyncStatus.CompletedWithWarnings, WarningCount = 4 });

        await Service().NotifyScheduledRunAsync(Result(9));

        Assert.That(_sentTo, Is.Empty);
    }

    [Test]
    public async Task Failed_run_alerts_with_its_error_summary()
    {
        Run(new AkeneoSyncRunRecord { Id = 9, SyncStatusId = (int)SyncStatus.Failed, ErrorSummary = "Akeneo returned 401" });

        await Service().NotifyScheduledRunAsync(Result(9));

        Assert.Multiple(() =>
        {
            Assert.That(Token("Akeneo.Run.Status"), Is.EqualTo("failed"));
            Assert.That(Token("Akeneo.Run.Errors"), Is.EqualTo("Akeneo returned 401"));
        });
    }

    [Test]
    public async Task Run_that_could_not_start_alerts_with_the_result_message()
    {
        await Service().NotifyScheduledRunAsync(new AkeneoProductSyncExecutionResult
        {
            Success = false,
            ProfileDisabled = true,
            ProfileId = 3,
            Message = "This Akeneo sync profile is disabled."
        });

        Assert.Multiple(() =>
        {
            Assert.That(Token("Akeneo.Run.Status"), Is.EqualTo("could not start"));
            Assert.That(Token("Akeneo.Run.Errors"), Is.EqualTo("This Akeneo sync profile is disabled."));
        });
    }

    [Test]
    public async Task Run_already_in_progress_or_disabled_alerts_send_nothing()
    {
        await Service().NotifyScheduledRunAsync(new AkeneoProductSyncExecutionResult { AlreadyRunning = true });

        _settings.SyncAlertsEnabled = false;
        await Service().NotifyScheduledRunAsync(new AkeneoProductSyncExecutionResult { Success = false });

        Assert.That(_sentTo, Is.Empty);
    }

    [Test]
    public async Task Category_run_with_a_failed_category_lists_its_slug_error()
    {
        Run(new AkeneoSyncRunRecord { Id = 12, SyncStatusId = (int)SyncStatus.CompletedWithErrors });
        var result = new AkeneoCategoryContentRunResult { Success = false, SyncRunRecordId = 12 };
        result.Categories.Add(new AkeneoCategoryContentItemResult { AkeneoCode = "oaks", Status = AkeneoCategoryContentStatus.Updated });
        var failed = new AkeneoCategoryContentItemResult
        {
            AkeneoCode = "shade_trees",
            NopCategoryId = 41,
            NopCategoryName = "Shade Trees",
            Status = AkeneoCategoryContentStatus.Failed
        };
        failed.Errors.Add("The Akeneo URL slug 'shade-trees' is already used by category 'Trees' (ID 7), so nopCommerce would save 'shade-trees-2' instead.");
        result.Categories.Add(failed);

        await Service().NotifyScheduledCategoryContentRunAsync(result);

        Assert.Multiple(() =>
        {
            Assert.That(_sentTo, Is.EqualTo(new[] { "ops@example.com", "merch@example.com" }));
            Assert.That(Token("Akeneo.Run.Type"), Is.EqualTo("category content sync"));
            Assert.That(Token("Akeneo.Run.Status"), Is.EqualTo("completed with failed categories"));
            Assert.That(Token("Akeneo.Run.Counts"), Is.EqualTo("Categories: updated 1, unchanged 0, skipped 0, failed 1."));
            Assert.That(Token("Akeneo.Run.Errors"), Is.EqualTo("-"));
            Assert.That(Token("Akeneo.Run.FailedItems"), Does.Contain("shade_trees")
                .And.Contain("Shade Trees (ID 41)")
                .And.Contain("&#39;shade-trees-2&#39;")
                .And.Contain("Akeneo category")
                .And.Not.Contain("oaks"));
        });
        _workflow.Verify(service => service.SendNotificationAsync(
            It.Is<MessageTemplate>(template => template.Name == AkeneoConnectionConstants.SyncRunAlertMessageTemplateName),
            It.IsAny<EmailAccount>(), It.IsAny<int>(), It.IsAny<IList<Token>>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Exactly(2));
    }

    [Test]
    public async Task Category_run_that_could_not_start_alerts_with_its_message()
    {
        await Service().NotifyScheduledCategoryContentRunAsync(new AkeneoCategoryContentRunResult
        {
            Success = false,
            Message = "Category content sync failed: Akeneo returned 401"
        });

        Assert.Multiple(() =>
        {
            Assert.That(Token("Akeneo.Run.Status"), Is.EqualTo("could not start"));
            Assert.That(Token("Akeneo.Run.Errors"), Is.EqualTo("Category content sync failed: Akeneo returned 401"));
        });
    }

    [Test]
    public async Task Category_run_that_succeeded_was_skipped_or_has_nothing_to_sync_sends_nothing()
    {
        var clean = new AkeneoCategoryContentRunResult { SyncRunRecordId = 12 };
        clean.Categories.Add(new AkeneoCategoryContentItemResult { AkeneoCode = "oaks", Status = AkeneoCategoryContentStatus.Unchanged });

        await Service().NotifyScheduledCategoryContentRunAsync(clean);
        await Service().NotifyScheduledCategoryContentRunAsync(new AkeneoCategoryContentRunResult { Message = "No category content mappings are set up yet." });
        await Service().NotifyScheduledCategoryContentRunAsync(new AkeneoCategoryContentRunResult { Success = false, AlreadyRunning = true });

        Assert.That(_sentTo, Is.Empty);
    }

    [Test]
    public async Task Scheduled_category_task_alerts_and_rethrows_when_the_sync_throws()
    {
        var sync = new Mock<IAkeneoCategoryContentSyncService>();
        sync.Setup(service => service.SyncAsync(null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var alerts = new Mock<IAkeneoSyncAlertService>();
        var task = new AkeneoCategoryContentSyncScheduleTask(sync.Object, alerts.Object, Mock.Of<ILogger>());

        Assert.ThrowsAsync<InvalidOperationException>(task.ExecuteAsync);

        alerts.Verify(service => service.NotifyScheduledCategoryContentRunAsync(
            It.Is<AkeneoCategoryContentRunResult>(result => !result.Success && result.Message.Contains("boom"))), Times.Once);
    }

    [Test]
    public async Task Stale_sync_alerts_once_per_threshold()
    {
        LastCompleted(Now.AddHours(-30));

        await Service().CheckForStaleSyncAsync(Now);

        Assert.Multiple(() =>
        {
            Assert.That(_sentTo, Has.Count.EqualTo(2));
            Assert.That(Token("Akeneo.Stale.Hours"), Is.EqualTo("30"));
            Assert.That(_settings.SyncAlertLastStaleNotificationUtc, Is.EqualTo(Now));
        });

        _sentTo.Clear();
        await Service().CheckForStaleSyncAsync(Now.AddHours(1));
        Assert.That(_sentTo, Is.Empty);

        await Service().CheckForStaleSyncAsync(Now.AddHours(26));
        Assert.That(_sentTo, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Recent_sync_or_disabled_schedule_is_not_stale()
    {
        LastCompleted(Now.AddHours(-2));
        await Service().CheckForStaleSyncAsync(Now);

        LastCompleted(Now.AddHours(-30));
        _syncTask.Enabled = false;
        await Service().CheckForStaleSyncAsync(Now);

        Assert.That(_sentTo, Is.Empty);
    }

    [Test]
    public async Task Never_completed_sync_is_measured_from_when_the_schedule_was_enabled()
    {
        _syncTask.LastEnabledUtc = Now.AddHours(-27);

        await Service().CheckForStaleSyncAsync(Now);

        Assert.That(Token("Akeneo.Stale.LastCompletedOn"), Is.EqualTo("never"));
    }

    [Test]
    public void Scheduled_run_that_throws_alerts_and_rethrows()
    {
        var alerts = new Mock<IAkeneoSyncAlertService>();
        var error = new InvalidOperationException("boom");

        Assert.ThrowsAsync<InvalidOperationException>(() => AkeneoScheduledSyncRunner.RunAsync(
            () => throw error,
            alerts.Object));

        alerts.Verify(service => service.NotifyScheduledRunErrorAsync(error), Times.Once);
        alerts.Verify(service => service.NotifyScheduledRunAsync(It.IsAny<AkeneoProductSyncExecutionResult>()), Times.Never);
    }

    [Test]
    public void Recipients_are_split_validated_and_deduplicated()
    {
        Assert.That(
            AkeneoSyncAlertService.ParseRecipients("a@example.com, not-an-email;B@example.com\nA@example.com"),
            Is.EqualTo(new[] { "a@example.com", "B@example.com" }));
    }

    private void Run(AkeneoSyncRunRecord run)
    {
        _runs.Setup(service => service.GetAkeneoSyncRunRecordByIdAsync(run.Id)).ReturnsAsync(run);
    }

    private void LastCompleted(DateTime finishedOnUtc)
    {
        var run = new AkeneoSyncRunRecord
        {
            Id = 4,
            SyncStatusId = (int)SyncStatus.Completed,
            FinishedOnUtc = finishedOnUtc
        };
        _runs
            .Setup(service => service.GetLastFinishedRunAsync(3, It.IsAny<IReadOnlyCollection<SyncType>>(), It.IsAny<bool>()))
            .ReturnsAsync(run);
    }

    private static AkeneoProductSyncExecutionResult Result(int runId) => new()
    {
        Success = true,
        ProfileId = 3,
        SyncRunRecordId = runId
    };

    private string Token(string key) =>
        _sentTokens.Last().Single(token => token.Key == key).Value.ToString()!;

    private AkeneoSyncAlertService Service()
    {
        var store = new Store { Id = 1, Url = "https://shop.example.com/" };
        var storeContext = new Mock<IStoreContext>();
        storeContext.Setup(context => context.GetCurrentStoreAsync()).ReturnsAsync(store);

        var templates = new Mock<IMessageTemplateService>();
        templates
            .Setup(service => service.GetMessageTemplatesByNameAsync(It.IsAny<string>(), 1))
            .ReturnsAsync((string name, int? _) => new List<MessageTemplate>
            {
                new() { Name = name, IsActive = true, EmailAccountId = 2 }
            });

        var emailAccounts = new Mock<IEmailAccountService>();
        emailAccounts.Setup(service => service.GetEmailAccountByIdAsync(2)).ReturnsAsync(new EmailAccount { Id = 2 });

        var languages = new Mock<ILanguageService>();
        languages
            .Setup(service => service.GetAllLanguagesAsync(It.IsAny<bool>(), It.IsAny<int>()))
            .ReturnsAsync(new List<Language> { new() { Id = 1 } });

        var dateTimeHelper = new Mock<IDateTimeHelper>();
        dateTimeHelper
            .Setup(helper => helper.ConvertToUserTimeAsync(It.IsAny<DateTime>(), DateTimeKind.Utc))
            .ReturnsAsync((DateTime value, DateTimeKind _) => value);

        return new AkeneoSyncAlertService(
            _settings,
            new EmailAccountSettings(),
            _itemLogs.Object,
            Mock.Of<IAkeneoSyncProfileService>(),
            _runs.Object,
            dateTimeHelper.Object,
            emailAccounts.Object,
            languages.Object,
            Mock.Of<ILogger>(),
            templates.Object,
            Mock.Of<IMessageTokenProvider>(),
            _scheduleTasks.Object,
            _settingService.Object,
            storeContext.Object,
            _workflow.Object);
    }
}
