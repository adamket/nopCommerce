using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services.DryRun;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Moq;
using Nop.Core.Domain.Catalog;
using Nop.Data;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

[TestFixture]
public class AkeneoProfileRulesTests
{
    private const string SkipRules = """[{"attributeCode":"sync_status","value":"hold"}]""";
    private static AkeneoProductImportRequest Request() => new()
    {
        SyncProfileId = 7, SyncRunRecordId = 23, AkeneoProductUuid = "uuid",
        Locale = "en_US", Channel = "ecommerce", Currency = "USD",
        SkipRulesJson = SkipRules, RunMode = AkeneoRunMode.Full
    };

    private static AkeneoProductDefinition Source(string? status, string? parent = null,
        string? channel = "ecommerce", string? locale = null) => new()
    {
        Uuid = "uuid", Identifier = "SKU", Parent = parent, Enabled = true,
        Values = JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            ["sync_status"] = new[] { new { data = status, scope = channel, locale } }
        })
    };

    [TestCase("other", null)]
    [TestCase("ecommerce", "fr_FR")]
    public async Task Rules_never_fall_back_to_another_channel_or_locale(string channel, string? locale)
    {
        var evaluator = new AkeneoProfileRuleEvaluator(Mock.Of<IAkeneoApiClient>());
        Assert.That(await evaluator.GetSkipReasonAsync(Source("hold", channel: channel, locale: locale), Request()), Is.Null);
    }

    [Test]
    public async Task Held_parent_blocks_descendant_even_with_child_override()
    {
        var api = new Mock<IAkeneoApiClient>();
        var parent = Source("hold"); parent.Code = "model";
        api.Setup(x => x.GetProductModelByCodeAsync("model", It.IsAny<CancellationToken>())).ReturnsAsync(parent);
        var reason = await new AkeneoProfileRuleEvaluator(api.Object)
            .GetSkipReasonAsync(Source("published", "model"), Request());
        Assert.That(reason, Does.Contain("hold"));
    }

    [Test]
    public void Unresolved_parent_fails_closed()
    {
        var evaluator = new AkeneoProfileRuleEvaluator(Mock.Of<IAkeneoApiClient>());
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await evaluator.GetSkipReasonAsync(Source("published", "missing"), Request()));
    }

    [Test]
    public async Task Multiple_skip_rules_are_or_conditions_and_do_not_match_substrings()
    {
        var request = Request();
        request.SkipRulesJson = """[{"attributeCode":"approval","value":"blocked"},{"attributeCode":"sync_status","value":"hold"}]""";
        var evaluator = new AkeneoProfileRuleEvaluator(Mock.Of<IAkeneoApiClient>());
        Assert.That(await evaluator.GetSkipReasonAsync(Source("HOLD"), request), Is.Not.Null);
        Assert.That(await evaluator.GetSkipReasonAsync(Source("on_hold"), request), Is.Null);
    }

    [Test]
    public async Task Profiles_without_rules_keep_existing_behavior()
    {
        var evaluator = new AkeneoProfileRuleEvaluator(new Mock<IAkeneoApiClient>(MockBehavior.Strict).Object);
        Assert.That(await evaluator.GetSkipReasonAsync(Source("hold", "unfetched"), new AkeneoProductImportRequest()), Is.Null);
    }

    [TestCase("not json")]
    [TestCase("null")]
    [TestCase("[null]")]
    [TestCase("[{}]")]
    [TestCase("[{\"attributeCode\":\"sync_status\",\"value\":\"hold\"},{\"attributeCode\":\"sync_status\",\"value\":\"hold\"}]")]
    public void Invalid_skip_configuration_is_rejected(string json) =>
        Assert.Throws<ArgumentException>(() => AkeneoProfileRules.ParseSkipRules(json));

    [Test]
    public void Profile_factory_carries_rules_and_changes_watermark_without_filtering_held_products_out()
    {
        var profile = new AkeneoSyncProfile { Id = 7, AkeneoChannel = "ecommerce", AkeneoLocales = "en_US" };
        var original = AkeneoSyncScopeHasher.Build(profile);
        profile.SkipRulesJson = SkipRules;
        Assert.That(AkeneoSyncScopeHasher.Build(profile), Is.Not.EqualTo(original));
        var request = new AkeneoProductBatchImportRequestFactory(new AkeneoProductSearchJsonBuilder())
            .CreateFromProfile(profile, 23);
        Assert.That(request.SkipRulesJson, Is.EqualTo(SkipRules));
        Assert.That(request.AdditionalSearchJson, Is.Null);
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task Batch_hold_has_no_destination_writes_and_only_marks_existing_binding_seen(bool hasBinding, bool inherited)
    {
        var api = new Mock<IAkeneoApiClient>();
        var stateService = new Mock<IAkeneoProductSyncStateService>(MockBehavior.Strict);
        var previous = hasBinding ? new AkeneoProductSyncState
        {
            Id = 42, NopProductId = 100, NopProductAttributeCombinationId = 200,
            DestinationKindId = (int)AkeneoProductDestinationKind.ProductAttributeCombination,
            LastDesiredStateHash = "existing-hash", SyncProfileId = 7
        } : null;
        var source = Source("hold");
        if (inherited)
        {
            source.Parent = "model";
            source.Values = JsonSerializer.SerializeToElement(new { });
            var parent = Source("hold"); parent.Code = "model";
            api.Setup(x => x.GetProductModelByCodeAsync("model", It.IsAny<CancellationToken>())).ReturnsAsync(parent);
        }
        api.Setup(x => x.GetProductByUuidAsync("uuid", It.IsAny<CancellationToken>())).ReturnsAsync(source);
        stateService.Setup(x => x.GetBySourceAsync(7, AkeneoEntityType.Product, "SKU", "uuid")).ReturnsAsync(previous);
        if (hasBinding)
            stateService.Setup(x => x.MarkSkippedSeenAsync(previous!, 23)).Returns(Task.CompletedTask);
        var service = new AkeneoProductBatchSyncService(
            api.Object, new Mock<IAkeneoProductValueResolver>(MockBehavior.Strict).Object,
            new Mock<IAkeneoNopEntityMappingService>(MockBehavior.Strict).Object,
            Mock.Of<IAkeneoSyncItemLogService>(), new Mock<IProductService>(MockBehavior.Strict).Object,
            new Mock<IAkeneoFamilyMappingService>(MockBehavior.Strict).Object,
            new AkeneoProductModelHierarchyResolver(),
            new Mock<IAkeneoVariantRelationshipService>(MockBehavior.Strict).Object,
            new Mock<IAkeneoLeafRepresentationClassifier>(MockBehavior.Strict).Object,
            new Mock<IAkeneoProductSyncService>(MockBehavior.Strict).Object, stateService.Object,
            new Mock<IAkeneoVariantRepresentationCleanupService>(MockBehavior.Strict).Object,
            new Mock<IAkeneoCatalogReconciliationService>(MockBehavior.Strict).Object,
            Mock.Of<IAkeneoSyncLeaseService>(), Mock.Of<IAkeneoProductModelDeltaFanOutService>(),
            new Mock<IAkeneoDryRunChangeAnalyzer>(MockBehavior.Strict).Object,
            new Mock<IAkeneoAssetMappingService>(MockBehavior.Strict).Object,
            new Mock<IAkeneoAssetResolver>(MockBehavior.Strict).Object, new AkeneoProfileRuleEvaluator(api.Object));
        var result = await service.SyncProductByUuidAsync(Request());
        Assert.That(result.Errors, Is.Empty);
        Assert.That(result.ActionType, Is.EqualTo(SyncItemActionType.Skipped));
        Assert.That(result.NopProductId, Is.EqualTo(hasBinding ? 100 : 0));
        Assert.That(result.NopProductAttributeCombinationId, Is.EqualTo(hasBinding ? 200 : null));
        stateService.VerifyAll();
        stateService.VerifyNoOtherCalls();
    }

    [Test]
    public async Task Mark_skipped_seen_preserves_destination_hash_and_lifecycle()
    {
        var repo = new Mock<IRepository<AkeneoProductSyncState>>();
        var state = new AkeneoProductSyncState
        {
            Id = 42, NopProductId = 100, NopParentProductId = 90,
            NopProductAttributeCombinationId = 200, NopProductAttributeValueId = 300,
            LastDesiredStateHash = "old", LastSeenRunRecordId = 1,
            LifecycleStatusId = (int)AkeneoProductLifecycleStatus.Unpublished
        };
        await new AkeneoProductSyncStateService(repo.Object).MarkSkippedSeenAsync(state, 23);
        Assert.Multiple(() =>
        {
            Assert.That(state.LastSeenRunRecordId, Is.EqualTo(23));
            Assert.That(state.LastDesiredStateHash, Is.EqualTo("old"));
            Assert.That(state.NopProductId, Is.EqualTo(100));
            Assert.That(state.NopParentProductId, Is.EqualTo(90));
            Assert.That(state.NopProductAttributeCombinationId, Is.EqualTo(200));
            Assert.That(state.NopProductAttributeValueId, Is.EqualTo(300));
            Assert.That(state.LifecycleStatusId, Is.EqualTo((int)AkeneoProductLifecycleStatus.Unpublished));
        });
        repo.Verify(x => x.UpdateAsync(state, true), Times.Once);
    }

    [Test]
    public async Task Held_dry_run_has_only_a_skip_operation_and_never_invokes_section_planners()
    {
        var planner = new Mock<IAkeneoDryRunSectionPlanner>(MockBehavior.Strict);
        planner.SetupGet(x => x.Order).Returns(100);
        var analyzer = new AkeneoDryRunChangeAnalyzer(new[] { planner.Object });
        var model = new AkeneoProductMappingPreviewModel();
        await analyzer.AnalyzeAsync(new AkeneoProductSyncContext { SkipReason = "sync_status equals hold" }, model);
        Assert.That(model.ImportAllowedByProfile, Is.False);
        Assert.That(model.PlannedChangeCount, Is.Zero);
        Assert.That(model.Operations.Single().ChangeType, Is.EqualTo(AkeneoDryRunOperationType.Skip));
    }



}

