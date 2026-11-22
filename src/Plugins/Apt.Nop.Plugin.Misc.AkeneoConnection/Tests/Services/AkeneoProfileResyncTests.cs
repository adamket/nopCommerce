using Microsoft.Extensions.DependencyInjection;
using System.Linq.Expressions;
using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.EventConsumers;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Nop.Core.Events;
using Nop.Data;
using Nop.Services.Configuration;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

/// <summary>
/// A resync reads the whole profile scope like a full run, but never removes
/// products and, without Repair, still skips
/// products whose output is unchanged.
/// </summary>
[TestFixture]
public class AkeneoProfileResyncTests
{
    private readonly AkeneoProductBatchImportRequestFactory _factory = new(new AkeneoProductSearchJsonBuilder());

    #region Request

    [Test]
    public void A_resync_reads_the_whole_scope_with_the_profiles_own_filters()
    {
        var profile = Profile();
        profile.UpdatedFilterModeId = (int)AkeneoUpdatedFilterMode.SinceLastSuccessfulRun;
        profile.IncludeLinkedAssetUpdates = true;
        profile.CompletenessFilterId = (int)AkeneoCompletenessFilter.None;

        var request = _factory.CreateFromProfile(profile, 1, DateTime.UtcNow.AddHours(-1), AkeneoRunMode.Resync);

        Assert.Multiple(() =>
        {
            Assert.That(request.UpdatedAfterUtc, Is.Null);
            Assert.That(request.IncludeLinkedAssetUpdates, Is.False);
            Assert.That(request.CompletenessFilter, Is.EqualTo(AkeneoCompletenessFilter.None), "a full run would force RequiredComplete");
            Assert.That(request.IsAuthoritativeFullRun, Is.False, "missing-product rules must never run");
            Assert.That(request.CreateNewProducts, Is.True);
            Assert.That(request.RepairDestination, Is.False);
        });
    }

    [Test]
    public void Resync_options_narrow_families_block_creation_and_turn_on_repair()
    {
        var profile = Profile();
        profile.AkeneoFamilyCodes = "coffee,tops,trees";

        var request = _factory.CreateFromProfile(profile, 1, runMode: AkeneoRunMode.Resync, resyncOptions: new AkeneoResyncOptions
        {
            FamilyCodes = ["coffee"],
            UpdateExistingOnly = true,
            RepairDestination = true
        });
        using var search = JsonDocument.Parse(request.SearchJson!);

        Assert.Multiple(() =>
        {
            Assert.That(request.AkeneoFamilyCodes, Is.EqualTo(new[] { "coffee" }));
            Assert.That(search.RootElement.GetProperty("family")[0].GetProperty("value")
                .EnumerateArray().Select(value => value.GetString()), Is.EqualTo(new[] { "coffee" }));
            Assert.That(request.CreateNewProducts, Is.False);
            Assert.That(request.UpdateExistingProducts, Is.True);
            Assert.That(request.RepairDestination, Is.True);
        });
    }

    [Test]
    public void Resync_options_are_ignored_outside_a_resync()
    {
        var request = _factory.CreateFromProfile(Profile(), 1, runMode: AkeneoRunMode.Delta,
            resyncOptions: new AkeneoResyncOptions { RepairDestination = true, UpdateExistingOnly = true, FamilyCodes = ["coffee"] });

        Assert.Multiple(() =>
        {
            Assert.That(request.RepairDestination, Is.False);
            Assert.That(request.CreateNewProducts, Is.True);
            Assert.That(request.AkeneoFamilyCodes, Is.Empty);
        });
    }

    [TestCase(AkeneoRunMode.Delta, false, true)]
    [TestCase(AkeneoRunMode.Resync, false, true)]
    [TestCase(AkeneoRunMode.Resync, true, false)]
    [TestCase(AkeneoRunMode.Full, false, false)]
    [TestCase(AkeneoRunMode.SingleProduct, false, false)]
    public void Only_delta_and_resync_without_repair_skip_unchanged_products(AkeneoRunMode mode, bool repair, bool skips)
    {
        var request = new AkeneoProductBatchImportRequest { RunMode = mode, RepairDestination = repair };

        Assert.That(AkeneoProductBatchSyncService.SkipsUnchangedWrites(request), Is.EqualTo(skips));
    }

    #endregion

    #region Execution

    private Mock<IAkeneoProductBatchSyncService> _batch = null!;
    private Mock<IAkeneoSyncRunRecordService> _runs = null!;
    private Mock<IAkeneoSyncProfileService> _profiles = null!;
    private Mock<IAkeneoSyncLeaseService> _leases = null!;
    private AkeneoSyncProfile _profile = null!;
    private AkeneoProductBatchImportRequest? _sentRequest;
    private AkeneoSyncRunRecord? _insertedRun;

    [SetUp]
    public void SetUp()
    {
        _batch = new Mock<IAkeneoProductBatchSyncService>();
        _runs = new Mock<IAkeneoSyncRunRecordService>();
        _profiles = new Mock<IAkeneoSyncProfileService>();
        _leases = new Mock<IAkeneoSyncLeaseService>();
        _profile = Profile();
        _profile.AkeneoFamilyCodes = "coffee,tops";
        _sentRequest = null;
        _insertedRun = null;

        _profiles.Setup(s => s.GetAkeneoSyncProfileByIdAsync(_profile.Id)).ReturnsAsync(() => _profile);
        _leases.Setup(s => s.TryAcquireAsync(It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(new AkeneoSyncLease { Id = 3 });
        _runs.Setup(s => s.InsertAkeneoSyncRunRecordAsync(It.IsAny<AkeneoSyncRunRecord>()))
            .Callback<AkeneoSyncRunRecord>(run => { run.Id = 41; _insertedRun = run; })
            .Returns(Task.CompletedTask);
        _batch.Setup(s => s.SyncProductsAsync(It.IsAny<AkeneoProductBatchImportRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AkeneoProductBatchImportRequest, CancellationToken>((request, _) => _sentRequest = request)
            .ReturnsAsync(new AkeneoProductBatchImportResult { CompletedAllPages = true });
    }

    [TestCase(SyncType.DeltaSync)]
    [TestCase(SyncType.ManualProfileSync)]
    public async Task Stop_cancels_the_named_run_and_retains_the_lease_until_the_worker_returns(SyncType type)
    {
        const string reason = "Stop requested by administrator (customer ID 7).";
        var logs = new Mock<IAkeneoSyncItemLogService>();
        logs.Setup(x => x.GetStopRequestAsync(41)).ReturnsAsync(reason);
        using var services = new ServiceCollection().AddSingleton(logs.Object).BuildServiceProvider();
        var workerStopping = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishCleanup = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _batch.Setup(x => x.SyncProductsAsync(It.IsAny<AkeneoProductBatchImportRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (AkeneoProductBatchImportRequest _, CancellationToken token) =>
            {
                using var registration = token.Register(() => workerStopping.TrySetResult(true));
                await finishCleanup.Task;
                token.ThrowIfCancellationRequested();
                return new AkeneoProductBatchImportResult();
            });
        var execution = new AkeneoProductSyncExecutionService(_batch.Object, _runs.Object, _factory, _profiles.Object,
            _leases.Object, stopScopes: services.GetRequiredService<IServiceScopeFactory>());
        var run = execution.ImportProductsByProfileAsync(_profile.Id, type);
        try
        {
            await workerStopping.Task.WaitAsync(TimeSpan.FromSeconds(10));
            _leases.Verify(x => x.ReleaseAsync(It.IsAny<AkeneoSyncLease>()), Times.Never);
        }
        finally
        {
            finishCleanup.TrySetResult(true);
        }
        var result = await run;
        Assert.That(result.SyncStatus, Is.EqualTo(SyncStatus.Cancelled));
        Assert.That(_insertedRun!.ErrorSummary, Does.Contain(reason));
        _leases.Verify(x => x.ReleaseAsync(It.Is<AkeneoSyncLease>(l => l.Id == 3)), Times.Once);
    }

    private AkeneoProductSyncExecutionService Execution() =>
        new(_batch.Object, _runs.Object, _factory, _profiles.Object, _leases.Object);

    [Test]
    public async Task A_whole_profile_resync_is_recorded_as_a_resync_and_establishes_a_checkpoint()
    {
        var result = await Execution().ResyncProfileAsync(_profile.Id, new AkeneoResyncOptions());

        Assert.Multiple(() =>
        {
            Assert.That(_insertedRun!.SyncTypeId, Is.EqualTo((int)SyncType.ManualResync));
            Assert.That(_insertedRun.RunModeId, Is.EqualTo((int)AkeneoRunMode.Resync));
            Assert.That(_insertedRun.ResyncCheckpointEligible, Is.True);
            Assert.That(_sentRequest!.RunMode, Is.EqualTo(AkeneoRunMode.Resync));
            Assert.That(_sentRequest.AkeneoFamilyCodes, Is.EqualTo(new[] { "coffee", "tops" }));
            Assert.That(result.Messages.First(), Does.StartWith("Resync of the whole profile"));
        });
        _runs.Verify(s => s.GetLastSuccessfulRunAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task A_family_resync_is_recorded_separately_and_only_reads_those_families()
    {
        var result = await Execution().ResyncProfileAsync(_profile.Id, new AkeneoResyncOptions
        {
            FamilyCodes = [" Coffee ", "coffee"],
            RepairDestination = true
        });

        Assert.Multiple(() =>
        {
            Assert.That(_insertedRun!.SyncTypeId, Is.EqualTo((int)SyncType.ManualFamilyResync));
            Assert.That(_sentRequest!.AkeneoFamilyCodes, Is.EqualTo(new[] { "Coffee" }));
            Assert.That(_sentRequest.RepairDestination, Is.True);
            Assert.That(result.Messages.First(), Does.Contain("families Coffee").And.Contain("Repair on"));
        });
    }

    [Test]
    public async Task A_family_outside_the_profile_is_refused_before_anything_runs()
    {
        var result = await Execution().ResyncProfileAsync(_profile.Id, new AkeneoResyncOptions { FamilyCodes = ["coffee", "trees"] });

        Assert.Multiple(() =>
        {
            Assert.That(result.InvalidRequest, Is.True);
            Assert.That(result.Message, Does.Contain("trees"));
        });
        _leases.Verify(s => s.TryAcquireAsync(It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Never);
        _batch.Verify(s => s.SyncProductsAsync(It.IsAny<AkeneoProductBatchImportRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task A_profile_without_families_can_be_narrowed_to_any_family()
    {
        _profile.AkeneoFamilyCodes = string.Empty;

        var result = await Execution().ResyncProfileAsync(_profile.Id, new AkeneoResyncOptions { FamilyCodes = ["trees"] });

        Assert.That(result.InvalidRequest, Is.False);
        Assert.That(_sentRequest!.AkeneoFamilyCodes, Is.EqualTo(new[] { "trees" }));
    }

    [Test]
    public async Task A_legacy_resync_without_explicit_eligibility_never_becomes_the_delta_watermark()
    {
        var resync = new AkeneoSyncRunRecord
        {
            Id = 2, SyncProfileId = 7, RunModeId = (int)AkeneoRunMode.Resync, SyncStatusId = (int)SyncStatus.Completed,
            CompletedAllPages = true, WatermarkUtc = DateTime.UtcNow
        };
        var delta = new AkeneoSyncRunRecord
        {
            Id = 1, SyncProfileId = 7, RunModeId = (int)AkeneoRunMode.Delta, SyncStatusId = (int)SyncStatus.Completed,
            CompletedAllPages = true, WatermarkUtc = DateTime.UtcNow.AddHours(-1)
        };
        var repository = new Mock<IRepository<AkeneoSyncRunRecord>>();
        repository.Setup(r => r.Table).Returns(new[] { resync, delta }.AsQueryable());

        var last = await new AkeneoSyncRunRecordService(repository.Object).GetLastSuccessfulRunAsync(7);

        Assert.That(last!.Id, Is.EqualTo(delta.Id));
    }

    #endregion

    [TestCase(false, false, false, true)]
    [TestCase(true, false, false, false)]
    [TestCase(false, true, false, false)]
    [TestCase(false, false, true, true)]
    public async Task Only_whole_profile_resyncs_without_write_overrides_are_eligible(
        bool narrowFamilies, bool existingOnly, bool repair, bool expected)
    {
        await Execution().ResyncProfileAsync(_profile.Id, new AkeneoResyncOptions
        {
            FamilyCodes = narrowFamilies ? new List<string> { "coffee" } : new List<string>(),
            UpdateExistingOnly = existingOnly,
            RepairDestination = repair
        });
        Assert.That(_insertedRun!.ResyncCheckpointEligible, Is.EqualTo(expected));
    }

    [TestCase("failed")]
    [TestCase("error")]
    [TestCase("canceled")]
    [TestCase("truncated")]
    [TestCase("incomplete")]
    [TestCase("exception")]
    public async Task Unsuccessful_or_incomplete_resyncs_never_establish_a_checkpoint(string failure)
    {
        var batch = new AkeneoProductBatchImportResult
        {
            CompletedAllPages = failure != "incomplete",
            WasTruncated = failure == "truncated",
            Canceled = failure == "canceled",
            FailedCount = failure == "failed" ? 1 : 0
        };
        if (failure == "error") batch.Errors.Add("Source page failed");
        var setup = _batch.Setup(service => service.SyncProductsAsync(It.IsAny<AkeneoProductBatchImportRequest>(), It.IsAny<CancellationToken>()));
        if (failure == "exception") setup.ThrowsAsync(new InvalidOperationException("Connection lost"));
        else setup.ReturnsAsync(batch);

        await Execution().ResyncProfileAsync(_profile.Id, new AkeneoResyncOptions());

        Assert.That(_insertedRun!.ResyncCheckpointEligible, Is.False);
    }

    [TestCase(0)]
    [TestCase(1)]
    public async Task Expanded_profile_resync_becomes_next_delta_start_with_overlap(int warnings)
    {
        var oldScope = AkeneoSyncScopeHasher.Build(_profile, AkeneoRunMode.Delta);
        _profile.AkeneoFamilyCodes += ",trees";
        _batch.Setup(service => service.SyncProductsAsync(It.IsAny<AkeneoProductBatchImportRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AkeneoProductBatchImportRequest, CancellationToken>((request, _) => _sentRequest = request)
            .ReturnsAsync(new AkeneoProductBatchImportResult { CompletedAllPages = true, WarningCount = warnings });
        await Execution().ResyncProfileAsync(_profile.Id, new AkeneoResyncOptions());
        var resync = _insertedRun!;
        var repository = new Mock<IRepository<AkeneoSyncRunRecord>>();
        repository.Setup(repo => repo.Table).Returns(new[] { resync }.AsQueryable());
        var records = new AkeneoSyncRunRecordService(repository.Object);
        _runs.Setup(service => service.GetLastSuccessfulRunAsync(_profile.Id, It.IsAny<string>()))
            .Returns((int id, string scope) => records.GetLastSuccessfulRunAsync(id, scope));

        await Execution().ImportProductsByProfileAsync(_profile.Id, SyncType.DeltaSync);

        Assert.Multiple(() =>
        {
            Assert.That(resync.ResyncCheckpointEligible, Is.True);
            Assert.That(resync.ScopeHash, Is.Not.EqualTo(oldScope));
            Assert.That(resync.WatermarkUtc, Is.EqualTo(resync.StartedOnUtc));
            Assert.That(_sentRequest!.UpdatedAfterUtc, Is.EqualTo(resync.StartedOnUtc.AddMinutes(-2)));
        });
        Assert.That(await records.GetLastSuccessfulRunAsync(_profile.Id, oldScope), Is.Null);
    }

    [TestCase(SyncStatus.Failed, true, false, true, SyncType.ManualResync, 0)]
    [TestCase(SyncStatus.Cancelled, true, false, true, SyncType.ManualResync, 0)]
    [TestCase(SyncStatus.CompletedWithErrors, true, false, true, SyncType.ManualResync, 1)]
    [TestCase(SyncStatus.Completed, false, false, true, SyncType.ManualResync, 0)]
    [TestCase(SyncStatus.Completed, true, true, true, SyncType.ManualResync, 0)]
    [TestCase(SyncStatus.Completed, true, false, false, SyncType.ManualResync, 0)]
    [TestCase(SyncStatus.Completed, true, false, true, SyncType.ManualFamilyResync, 0)]
    [TestCase(SyncStatus.Completed, true, false, true, SyncType.ManualResync, 1)]
    public async Task Unsafe_resync_records_are_excluded_from_checkpoint_selection(
        SyncStatus status, bool allPages, bool truncated, bool eligible, SyncType type, int failures)
    {
        var repository = new Mock<IRepository<AkeneoSyncRunRecord>>();
        repository.Setup(repo => repo.Table).Returns(new[] { new AkeneoSyncRunRecord
        {
            Id = 1, SyncProfileId = 7, ScopeHash = "scope", RunModeId = (int)AkeneoRunMode.Resync,
            SyncTypeId = (int)type, SyncStatusId = (int)status, CompletedAllPages = allPages,
            WasTruncated = truncated, ResyncCheckpointEligible = eligible, FailedCount = failures
        } }.AsQueryable());
        Assert.That(await new AkeneoSyncRunRecordService(repository.Object).GetLastSuccessfulRunAsync(7, "scope"), Is.Null);
    }

    #region Mappings changed

    [Test]
    public async Task Category_mappings_count_as_a_mapping_change_but_product_bindings_do_not()
    {
        var tracker = new Mock<IAkeneoMappingChangeTracker>();
        var consumer = new AkeneoMappingChangeConsumer(tracker.Object);

        await consumer.HandleEventAsync(new EntityInsertedEvent<AkeneoNopEntityMapping>(
            new AkeneoNopEntityMapping { AkeneoEntityTypeId = (int)AkeneoEntityType.Product }));
        await consumer.HandleEventAsync(new EntityInsertedEvent<AkeneoNopEntityMapping>(
            new AkeneoNopEntityMapping { AkeneoEntityTypeId = (int)AkeneoEntityType.Option }));
        tracker.Verify(t => t.MarkChangedAsync(), Times.Never);

        await consumer.HandleEventAsync(new EntityUpdatedEvent<AkeneoNopEntityMapping>(
            new AkeneoNopEntityMapping { AkeneoEntityTypeId = (int)AkeneoEntityType.Category }));
        await consumer.HandleEventAsync(new EntityDeletedEvent<AkeneoAttributeMapping>(new AkeneoAttributeMapping()));
        tracker.Verify(t => t.MarkChangedAsync(), Times.Exactly(2));
    }

    [Test]
    public async Task Rows_saved_together_write_the_timestamp_once()
    {
        DateTime? stored = null;
        var settings = new Mock<ISettingService>();
        settings.Setup(s => s.GetSettingKey(It.IsAny<AkeneoConnectionSettings>(),
                It.IsAny<Expression<Func<AkeneoConnectionSettings, DateTime?>>>()))
            .Returns("akeneoconnectionsettings.mappingschangedonutc");
        settings.Setup(s => s.GetSettingByKeyAsync<DateTime?>("akeneoconnectionsettings.mappingschangedonutc", null, 0, false))
            .ReturnsAsync(() => stored);
        settings.Setup(s => s.SetSettingAsync<DateTime?>("akeneoconnectionsettings.mappingschangedonutc", It.IsAny<DateTime?>(), 0, true))
            .Callback<string, DateTime?, int, bool>((_, value, _, _) => stored = value)
            .Returns(Task.CompletedTask);
        var tracker = new AkeneoMappingChangeTracker(settings.Object);

        await tracker.MarkChangedAsync();
        await tracker.MarkChangedAsync();

        Assert.That(stored, Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromSeconds(5)));
        settings.Verify(s => s.SetSettingAsync<DateTime?>(It.IsAny<string>(), It.IsAny<DateTime?>(), 0, true), Times.Once);
    }

    #endregion

    private static AkeneoSyncProfile Profile() => new()
    {
        Id = 7,
        Name = "Default",
        Enabled = true,
        AkeneoChannel = "ecommerce",
        AkeneoLocales = "en_US",
        CurrencyCode = "USD",
        ProductWriteModeId = (int)AkeneoProductWriteMode.CreateAndUpdate,
        UnmappedAttributeBehaviorId = (int)UnmappedAkeneoAttributeBehavior.Ignore,
        PageSize = 100,
        ContinueOnError = true,
        AkeneoFamilyCodes = string.Empty,
        AkeneoCategoryCodes = string.Empty,
        AkeneoProductGroupCodes = string.Empty,
        CategoryFilterModeId = (int)AkeneoCategoryFilterMode.None,
        ProductEnabledFilterId = (int)AkeneoProductEnabledFilter.Any,
        ProductParentFilterModeId = (int)AkeneoProductParentFilterMode.Any,
        UpdatedFilterModeId = (int)AkeneoUpdatedFilterMode.SinceLastSuccessfulRun
    };
}
