using System.Linq.Expressions;
using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.ScheduleTasks;
using Nop.Core;
using Nop.Core.Domain.Configuration;
using Nop.Core.Domain.Messages;
using Nop.Core.Domain.ScheduleTasks;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.ScheduleTasks;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

[TestFixture]
public class AkeneoPluginInstallationTests
{
    private Mock<ISettingService> _settings = null!;
    private Dictionary<(string Name, int StoreId), object?> _values = null!;
    private List<(string Name, int StoreId)> _writes = null!;

    [SetUp]
    public void SetUp()
    {
        _settings = new Mock<ISettingService>(MockBehavior.Strict);
        _values = new();
        _writes = new();
        ConfigureSettings<string>();
        ConfigureSettings<int?>();
        ConfigureSettings<bool>();
        ConfigureSettings<int>();
        ConfigureSettings<DateTime?>();
    }

    [Test]
    public async Task Fresh_install_initializes_all_defaults_and_repeated_install_writes_nothing()
    {
        var plugin = Plugin();
        await plugin.InstallAsync();

        var defaults = new AkeneoConnectionSettings();
        var properties = typeof(AkeneoConnectionSettings).GetProperties();
        Assert.That(_values.Count, Is.EqualTo(properties.Length));
        foreach (var property in properties)
            Assert.That(
                _values[(property.Name, 0)],
                Is.EqualTo(property.GetValue(defaults)),
                property.Name
            );

        _writes.Clear();
        await plugin.InstallAsync();
        Assert.That(_writes, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Configured_database_keeps_settings_on_install_or_uninstall_and_reinstall(
        bool uninstallFirst
    )
    {
        foreach (var property in typeof(AkeneoConnectionSettings).GetProperties())
        {
            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            _values[(property.Name, 0)] =
                type == typeof(string) ? "configured-value"
                : type == typeof(bool) ? true
                : type == typeof(int) ? 42
                : new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
            _values[(property.Name, 2)] = _values[(property.Name, 0)];
        }

        var original = _values.ToArray();
        var plugin = Plugin();
        if (uninstallFirst)
        {
            await plugin.UninstallAsync();
            _settings.VerifyNoOtherCalls();
            Assert.That(_values, Is.EquivalentTo(original));
        }
        await plugin.InstallAsync();

        Assert.That(_writes, Is.Empty);
        Assert.That(_values, Is.EquivalentTo(original));
    }

    [Test]
    public async Task Partial_configuration_fills_missing_defaults_preserving_empty_zero_false_and_store_overrides()
    {
        _values[(nameof(AkeneoConnectionSettings.AkeneoConnectionBaseUrl), 0)] =
            "https://pim.example.com";
        _values[(nameof(AkeneoConnectionSettings.SyncAlertEmails), 0)] = "";
        _values[(nameof(AkeneoConnectionSettings.SyncAlertsEnabled), 0)] = false;
        _values[(nameof(AkeneoConnectionSettings.SyncAlertStaleAfterHours), 0)] = 0;
        _values[(nameof(AkeneoConnectionSettings.DefaultSyncProfileId), 0)] = null;
        _values[(nameof(AkeneoConnectionSettings.AkeneoConnectionClientId), 2)] = "store-client";
        var original = _values.ToArray();

        await Plugin().InstallAsync();

        foreach (var entry in original)
        {
            Assert.That(_values[entry.Key], Is.EqualTo(entry.Value), entry.Key.ToString());
            Assert.That(_writes, Does.Not.Contain(entry.Key));
        }
        Assert.That(
            _values[(nameof(AkeneoConnectionSettings.AkeneoConnectionClientId), 0)],
            Is.Null
        );
        Assert.That(
            _values[(nameof(AkeneoConnectionSettings.SyncAlertMinimumFailedProducts), 0)],
            Is.EqualTo(1)
        );
        Assert.That(
            _values.Count,
            Is.EqualTo(typeof(AkeneoConnectionSettings).GetProperties().Length + 1)
        );
        Assert.That(_writes.All(key => key.StoreId == 0), Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Uninstall_and_reinstall_preserve_task_configuration_and_custom_templates(
        bool retryUninstall
    )
    {
        var taskTypes = new[]
        {
            typeof(AkeneoProductSyncScheduleTask),
            typeof(AkeneoProductFullSyncScheduleTask),
            typeof(AkeneoSyncLogRetentionScheduleTask),
            typeof(AkeneoCategoryContentSyncScheduleTask),
            typeof(AkeneoStaleSyncCheckScheduleTask),
        };
        var tasks = taskTypes
            .Select(
                (type, index) =>
                    new ScheduleTask
                    {
                        Id = index + 1,
                        Type = type.FullName!,
                        Name = $"Custom task {index}",
                        Seconds = 1234 + index,
                        Enabled = index % 2 == 0,
                        StopOnError = index % 2 == 1,
                        LastEnabledUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
                        LastStartUtc = new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc),
                        LastEndUtc = new DateTime(2026, 10, 2, 0, 1, 0, DateTimeKind.Utc),
                        LastSuccessUtc = new DateTime(2026, 10, 2, 0, 1, 0, DateTimeKind.Utc),
                    }
            )
            .ToList();
        tasks.Add(
            new ScheduleTask
            {
                Id = 99,
                Type = "OtherPlugin.Task",
                Enabled = true,
            }
        );
        var originalTasks = JsonSerializer.Serialize(tasks);
        var scheduleService = new Mock<IScheduleTaskService>(MockBehavior.Strict);
        scheduleService
            .Setup(service => service.GetTaskByTypeAsync(It.IsAny<string>()))
            .ReturnsAsync((string type) => tasks.SingleOrDefault(task => task.Type == type)!);
        scheduleService
            .Setup(service => service.UpdateTaskAsync(It.IsAny<ScheduleTask>()))
            .Returns(Task.CompletedTask);

        var savedStates = new Dictionary<string, Setting>(StringComparer.OrdinalIgnoreCase);
        _settings
            .Setup(service => service.GetSettingAsync(It.IsAny<string>(), 0, false))
            .ReturnsAsync((string key, int _, bool _) => savedStates.GetValueOrDefault(key)!);
        _settings
            .Setup(service =>
                service.SetSettingAsync(It.IsAny<string>(), It.IsAny<bool>(), 0, true)
            )
            .Callback(
                (string key, bool enabled, int _, bool _) =>
                    savedStates[key] = new Setting
                    {
                        Name = key,
                        Value = enabled.ToString(),
                        StoreId = 0,
                    }
            )
            .Returns(Task.CompletedTask);
        _settings
            .Setup(service => service.DeleteSettingAsync(It.IsAny<Setting>()))
            .Callback((Setting setting) => savedStates.Remove(setting.Name))
            .Returns(Task.CompletedTask);

        var templateNames = new[]
        {
            AkeneoConnectionConstants.SyncRunAlertMessageTemplateName,
            AkeneoConnectionConstants.StaleSyncAlertMessageTemplateName,
            AkeneoConnectionConstants.SyncRunAlertMessageTemplateName,
        };
        var templates = templateNames
            .Select(
                (name, index) =>
                    new MessageTemplate
                    {
                        Id = 10 + index,
                        Name = name,
                        Subject = $"Custom subject {index}",
                        Body = $"<p>Custom body {index}</p>",
                        BccEmailAddresses = "audit@example.com",
                        EmailAccountId = 20 + index,
                        IsActive = index != 1,
                        LimitedToStores = index == 2,
                        AttachedDownloadId = 30 + index,
                    }
            )
            .ToList();
        var originalTemplates = JsonSerializer.Serialize(templates);
        var templateService = new Mock<IMessageTemplateService>(MockBehavior.Strict);
        templateService
            .Setup(service => service.GetMessageTemplatesByNameAsync(It.IsAny<string>(), null))
            .ReturnsAsync(
                (string name, int? _) =>
                    (IList<MessageTemplate>)
                        templates.Where(template => template.Name == name).ToList()
            );

        await Plugin(scheduleService.Object, templateService.Object).UninstallAsync();
        if (retryUninstall)
            await Plugin(scheduleService.Object, templateService.Object).UninstallAsync();

        Assert.That(tasks.Take(5).All(task => !task.Enabled), Is.True);
        Assert.That(tasks.Last().Enabled, Is.True, "Other plugins' tasks must be untouched.");
        Assert.That(savedStates.Count, Is.EqualTo(5));
        templateService.VerifyNoOtherCalls();
        Assert.That(JsonSerializer.Serialize(templates), Is.EqualTo(originalTemplates));

        await Plugin(scheduleService.Object, templateService.Object).InstallAsync();

        Assert.That(JsonSerializer.Serialize(tasks), Is.EqualTo(originalTasks));
        Assert.That(JsonSerializer.Serialize(templates), Is.EqualTo(originalTemplates));
        Assert.That(savedStates, Is.Empty);

        // Once restored, a later install must respect subsequent administrator changes.
        tasks[0].Enabled = false;
        await Plugin(scheduleService.Object, templateService.Object).InstallAsync();
        Assert.That(tasks[0].Enabled, Is.False);
    }

    [Test]
    public async Task Fresh_install_creates_missing_tasks_and_templates_only_once()
    {
        var tasks = new List<ScheduleTask>();
        var templates = new List<MessageTemplate>();
        var scheduleService = new Mock<IScheduleTaskService>(MockBehavior.Strict);
        scheduleService
            .Setup(service => service.GetTaskByTypeAsync(It.IsAny<string>()))
            .ReturnsAsync((string type) => tasks.SingleOrDefault(task => task.Type == type)!);
        scheduleService
            .Setup(service => service.InsertTaskAsync(It.IsAny<ScheduleTask>()))
            .Callback((ScheduleTask task) => tasks.Add(task))
            .Returns(Task.CompletedTask);
        var templateService = new Mock<IMessageTemplateService>(MockBehavior.Strict);
        templateService
            .Setup(service => service.GetMessageTemplatesByNameAsync(It.IsAny<string>(), null))
            .ReturnsAsync(
                (string name, int? _) =>
                    (IList<MessageTemplate>)
                        templates.Where(template => template.Name == name).ToList()
            );
        templateService
            .Setup(service => service.InsertMessageTemplateAsync(It.IsAny<MessageTemplate>()))
            .Callback((MessageTemplate template) => templates.Add(template))
            .Returns(Task.CompletedTask);
        _settings
            .Setup(service => service.GetSettingAsync(It.IsAny<string>(), 0, false))
            .ReturnsAsync((Setting)null!);

        await Plugin(scheduleService.Object, templateService.Object).InstallAsync();
        await Plugin(scheduleService.Object, templateService.Object).InstallAsync();

        Assert.That(tasks.Count, Is.EqualTo(4));
        Assert.That(templates.Count, Is.EqualTo(2));
        Assert.That(
            tasks
                .Single(task => task.Type == typeof(AkeneoProductSyncScheduleTask).FullName)
                .Enabled,
            Is.True
        );
        Assert.That(
            tasks.Any(task => task.Type == typeof(AkeneoProductFullSyncScheduleTask).FullName),
            Is.False,
            "The full reconciliation task must not be created."
        );
    }

    private void ConfigureSettings<T>()
    {
        _settings
            .Setup(service =>
                service.SettingExistsAsync(
                    It.IsAny<AkeneoConnectionSettings>(),
                    It.IsAny<Expression<Func<AkeneoConnectionSettings, T>>>(),
                    It.IsAny<int>()
                )
            )
            .ReturnsAsync(
                (
                    AkeneoConnectionSettings _,
                    Expression<Func<AkeneoConnectionSettings, T>> selector,
                    int storeId
                ) => _values.ContainsKey((((MemberExpression)selector.Body).Member.Name, storeId))
            );

        _settings
            .Setup(service =>
                service.SaveSettingAsync(
                    It.IsAny<AkeneoConnectionSettings>(),
                    It.IsAny<Expression<Func<AkeneoConnectionSettings, T>>>(),
                    It.IsAny<int>(),
                    It.IsAny<bool>()
                )
            )
            .Callback(
                (
                    AkeneoConnectionSettings settings,
                    Expression<Func<AkeneoConnectionSettings, T>> selector,
                    int storeId,
                    bool _
                ) =>
                {
                    var key = (((MemberExpression)selector.Body).Member.Name, storeId);
                    _values[key] = selector.Compile()(settings);
                    _writes.Add(key);
                }
            )
            .Returns(Task.CompletedTask);
    }

    private AkeneoConnectionPlugin Plugin(
        IScheduleTaskService? scheduleService = null,
        IMessageTemplateService? templateService = null
    )
    {
        var templates = new Mock<IMessageTemplateService>();
        templates
            .Setup(service =>
                service.GetMessageTemplatesByNameAsync(It.IsAny<string>(), It.IsAny<int?>())
            )
            .ReturnsAsync(new List<MessageTemplate>());

        return new AkeneoConnectionPlugin(
            _settings.Object,
            Mock.Of<IWebHelper>(),
            scheduleService ?? Mock.Of<IScheduleTaskService>(),
            Mock.Of<ILocalizationService>(),
            templateService ?? templates.Object,
            new EmailAccountSettings()
        );
    }
}
