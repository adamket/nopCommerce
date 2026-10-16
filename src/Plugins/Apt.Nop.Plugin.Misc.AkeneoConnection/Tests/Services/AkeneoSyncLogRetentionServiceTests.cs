using System.Linq.Expressions;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Nop.Core;
using Nop.Data;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

[TestFixture]
public class AkeneoSyncLogRetentionServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);

    private List<AkeneoSyncProfile> _profiles = null!;
    private List<AkeneoSyncRunRecord> _runs = null!;
    private List<AkeneoSyncItemLog> _logs = null!;
    private List<AkeneoSyncLease> _leases = null!;
    private AkeneoSyncLogRetentionService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _profiles = [];
        _runs = [];
        _logs = [];
        _leases = [];

        var profiles = new Mock<IAkeneoSyncProfileService>();
        profiles.Setup(s => s.GetAllAkeneoSyncProfilesAsync()).ReturnsAsync(() => _profiles.ToList());

        _service = new AkeneoSyncLogRetentionService(
            profiles.Object,
            Repository(_logs).Object,
            Repository(_runs).Object,
            Repository(_leases).Object);
    }

    [Test]
    public async Task Deletes_old_finished_runs_and_their_item_logs()
    {
        AddProfile(1);
        var old = AddRun(1, profileId: 1, daysAgo: 120, SyncStatus.Failed);
        var recent = AddRun(2, profileId: 1, daysAgo: 10, SyncStatus.Completed);
        AddLog(10, old.Id, daysAgo: 5);
        AddLog(11, recent.Id, daysAgo: 5);

        var result = await _service.TrimAsync(Now);

        Assert.That(_runs.Select(r => r.Id), Is.EquivalentTo(new[] { 2 }));
        Assert.That(_logs.Select(l => l.Id), Is.EquivalentTo(new[] { 11 }));
        Assert.That(result, Is.EqualTo(new AkeneoSyncLogRetentionResult(1, 1)));
    }

    [Test]
    public async Task Applies_each_profiles_own_retention_and_skips_disabled_profiles()
    {
        AddProfile(1, itemDays: 30, runDays: 90);
        AddProfile(2, itemDays: 7, runDays: 14);
        AddProfile(3, enabled: false, itemDays: 1, runDays: 1);
        AddRun(1, profileId: 1, daysAgo: 60, SyncStatus.Failed);
        AddRun(2, profileId: 2, daysAgo: 60, SyncStatus.Failed);
        AddRun(3, profileId: 3, daysAgo: 60, SyncStatus.Failed);
        AddRun(4, profileId: null, daysAgo: 600, SyncStatus.Failed);
        AddLog(10, runId: 1, daysAgo: 10);
        AddLog(11, runId: 3, daysAgo: 10);
        AddLog(12, runId: 4, daysAgo: 600);

        await _service.TrimAsync(Now);

        Assert.That(_runs.Select(r => r.Id), Is.EquivalentTo(new[] { 1, 3, 4 }));
        Assert.That(_logs.Select(l => l.Id), Is.EquivalentTo(new[] { 10, 11, 12 }));
    }

    [Test]
    public async Task Keeps_latest_successful_run_per_scope_even_when_old()
    {
        AddProfile(1);
        AddProfile(2);
        AddRun(1, profileId: 1, daysAgo: 200, SyncStatus.Completed, scopeHash: "a");
        AddRun(2, profileId: 1, daysAgo: 150, SyncStatus.Completed, scopeHash: "a");
        AddRun(3, profileId: 1, daysAgo: 140, SyncStatus.CompletedWithWarnings, scopeHash: "b");
        AddRun(4, profileId: 2, daysAgo: 130, SyncStatus.Completed, scopeHash: "a");
        // Newer, but not watermark candidates.
        AddRun(5, profileId: 1, daysAgo: 100, SyncStatus.Completed, scopeHash: "a", runMode: AkeneoRunMode.SingleProduct);
        AddRun(6, profileId: 1, daysAgo: 100, SyncStatus.Completed, scopeHash: "a", truncated: true);

        await _service.TrimAsync(Now);

        Assert.That(_runs.Select(r => r.Id), Is.EquivalentTo(new[] { 2, 3, 4 }));
    }

    [Test]
    public async Task Keeps_unfinished_and_leased_runs()
    {
        AddProfile(1);
        AddRun(1, profileId: 1, daysAgo: 120, SyncStatus.Started, finished: false);
        AddRun(2, profileId: 1, daysAgo: 120, SyncStatus.Failed);
        _leases.Add(new AkeneoSyncLease { Id = 1, SyncRunRecordId = 2 });

        await _service.TrimAsync(Now);

        Assert.That(_runs.Select(r => r.Id), Is.EquivalentTo(new[] { 1, 2 }));
    }

    [Test]
    public async Task Deletes_old_item_logs_of_kept_runs()
    {
        AddProfile(1);
        var run = AddRun(1, profileId: 1, daysAgo: 120, SyncStatus.Completed);
        AddLog(10, run.Id, daysAgo: 45);
        AddLog(11, run.Id, daysAgo: 29);

        var result = await _service.TrimAsync(Now);

        Assert.That(_runs, Has.Count.EqualTo(1));
        Assert.That(_logs.Select(l => l.Id), Is.EquivalentTo(new[] { 11 }));
        Assert.That(result, Is.EqualTo(new AkeneoSyncLogRetentionResult(1, 0)));
    }

    [Test]
    public async Task Deletes_more_than_one_batch()
    {
        AddProfile(1);
        var run = AddRun(1, profileId: 1, daysAgo: 1, SyncStatus.Completed);
        for (var i = 1; i <= AkeneoSyncLogRetentionService.BatchSize + 5; i++)
            AddLog(i, run.Id, daysAgo: 60);

        var result = await _service.TrimAsync(Now);

        Assert.That(_logs, Is.Empty);
        Assert.That(result.DeletedItemLogs, Is.EqualTo(AkeneoSyncLogRetentionService.BatchSize + 5));
    }

    [Test]
    public async Task Treats_retention_below_one_day_as_one_day()
    {
        var profile = AddProfile(1, itemDays: 0, runDays: 0);
        var run = AddRun(1, profileId: 1, daysAgo: 0, SyncStatus.Failed);
        AddLog(10, run.Id, daysAgo: 0);

        var result = await _service.TrimProfileAsync(profile, Now);

        Assert.That(result, Is.EqualTo(new AkeneoSyncLogRetentionResult(0, 0)));
    }

    [Test]
    public void Watermark_ties_are_broken_by_highest_id()
    {
        var watermark = Now.AddDays(-1);

        var kept = AkeneoSyncLogRetentionService.SelectWatermarkRunIds(
        [
            new AkeneoRunWatermark(1, "a", watermark),
            new AkeneoRunWatermark(2, "a", watermark)
        ]);

        Assert.That(kept, Is.EquivalentTo(new[] { 2 }));
    }

    private AkeneoSyncProfile AddProfile(int id, bool enabled = true, int itemDays = 30, int runDays = 90)
    {
        var profile = new AkeneoSyncProfile
        {
            Id = id,
            SyncLogRetentionEnabled = enabled,
            SyncItemLogRetentionDays = itemDays,
            SyncRunRecordRetentionDays = runDays
        };
        _profiles.Add(profile);
        return profile;
    }

    private AkeneoSyncRunRecord AddRun(
        int id,
        int? profileId,
        int daysAgo,
        SyncStatus status,
        string scopeHash = "scope",
        AkeneoRunMode runMode = AkeneoRunMode.Delta,
        bool finished = true,
        bool truncated = false)
    {
        var started = Now.AddDays(-daysAgo);
        var run = new AkeneoSyncRunRecord
        {
            Id = id,
            SyncProfileId = profileId,
            SyncStatusId = (int)status,
            RunModeId = (int)runMode,
            StartedOnUtc = started,
            WatermarkUtc = started,
            FinishedOnUtc = finished ? started.AddMinutes(5) : null,
            CompletedAllPages = true,
            WasTruncated = truncated,
            ScopeHash = scopeHash
        };
        _runs.Add(run);
        return run;
    }

    private void AddLog(int id, int runId, int daysAgo) =>
        _logs.Add(new AkeneoSyncItemLog { Id = id, SyncRunRecordId = runId, CreatedOnUtc = Now.AddDays(-daysAgo) });

    private static Mock<IRepository<T>> Repository<T>(List<T> rows) where T : BaseEntity
    {
        var repository = new Mock<IRepository<T>>();
        repository.SetupGet(r => r.Table).Returns(() => rows.ToList().AsQueryable());
        repository
            .Setup(r => r.DeleteAsync(It.IsAny<Expression<Func<T, bool>>>()))
            .ReturnsAsync((Expression<Func<T, bool>> predicate) => rows.RemoveAll(new Predicate<T>(predicate.Compile())));
        return repository;
    }
}
