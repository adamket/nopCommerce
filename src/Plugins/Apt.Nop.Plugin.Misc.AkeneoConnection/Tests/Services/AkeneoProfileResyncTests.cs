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
/// products, never moves the delta watermark and, without Repair, still skips
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

    private AkeneoProductSyncExecutionService Execution() =>
        new(_batch.Object, _runs.Object, _factory, _profiles.Object, _leases.Object);

    [Test]
    public async Task A_whole_profile_resync_is_recorded_as_a_resync_and_skips_the_watermark()
    {
        var result = await Execution().ResyncProfileAsync(_profile.Id, new AkeneoResyncOptions());

        Assert.Multiple(() =>
        {
            Assert.That(_insertedRun!.SyncTypeId, Is.EqualTo((int)SyncType.ManualResync));
            Assert.That(_insertedRun.RunModeId, Is.EqualTo((int)AkeneoRunMode.Resync));
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
    public async Task A_completed_resync_never_becomes_the_delta_watermark()
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
