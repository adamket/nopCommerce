using System.Text.Json;
using System.Transactions;
using Moq;
using NUnit.Framework;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

[TestFixture]
public class AkeneoCombinationReconciliationTests
{
    private Mock<IAkeneoApiClient> _api = null!;
    private Mock<IAkeneoFamilyMappingService> _families = null!;
    private Mock<IProductService> _products = null!;
    private Mock<IProductAttributeService> _attributes = null!;
    private Mock<IAkeneoManagedRelationService> _relations = null!;
    private Mock<IAkeneoNopEntityMappingService> _mappings = null!;
    private Mock<IAkeneoCombinationPictureSynchronizer> _pictures = null!;
    private Mock<IAkeneoAttributeValueCacheTracker> _cache = null!;
    private Mock<IAkeneoDeferredProductEvents> _events = null!;
    private Mock<IAkeneoSyncItemLogService> _logs = null!;
    private AkeneoCombinationReconciliationService _service = null!;
    private AkeneoProductBatchImportRequest _request = null!;
    private AkeneoProductBatchImportResult _result = null!;
    private List<ProductAttributeValue> _values = null!;
    private List<ProductAttributeCombination> _combinations = null!;
    private List<AkeneoProductDefinition> _branch = null!;
    private Product _parent = null!;

    [SetUp]
    public void SetUp()
    {
        _api = new(); _families = new(); _products = new(); _attributes = new();
        _relations = new(); _mappings = new(); _pictures = new(); _cache = new(); _events = new(); _logs = new();
        _request = new() { SyncProfileId = 1, SyncRunRecordId = 10, RunMode = AkeneoRunMode.Resync,
            ProductAttributeSyncMode = AkeneoCollectionSyncMode.ReplaceAll };
        _result = new() { CompletedAllPages = true };
        _parent = new() { Id = 1794 };
        _values = new() { Value(2045, "S"), Value(2047, "L"), Value(3523, "S"), Value(3593, "M") };
        _combinations = new() { Combo(515, 2045), Combo(517, 2047), Combo(41642, 3523, "9945-S"), Combo(41712, 3593, "9945-M") };
        _branch = new() { Source("9945-S"), Source("9945-M") };
        _api.Setup(api => api.GetProductsPageAsync(100, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AkeneoProductPageResult { Items = _branch });
        _api.Setup(api => api.GetProductModelsPageAsync(100, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AkeneoProductPageResult { Items = new List<AkeneoProductDefinition>() });
        _families.Setup(f => f.GetByIdAsync(29)).ReturnsAsync(new AkeneoFamilyMapping { Id = 29, Enabled = true });
        _families.Setup(f => f.GetAxisMappingsAsync(29)).ReturnsAsync(new List<AkeneoFamilyVariantAxisMapping>
        { new() { AkeneoAttributeCode = "garment_size", NopProductAttributeId = 10, IsRequired = true } });
        _products.Setup(p => p.GetProductByIdAsync(1794)).ReturnsAsync(_parent);
        _attributes.Setup(a => a.GetProductAttributeMappingsByProductIdAsync(1794)).ReturnsAsync(new List<ProductAttributeMapping>
        { new() { Id = 910, ProductId = 1794, ProductAttributeId = 10 } });
        _attributes.Setup(a => a.GetProductAttributeValuesAsync(910)).ReturnsAsync(() => _values.ToList());
        _attributes.Setup(a => a.GetAllProductAttributeCombinationsAsync(1794)).ReturnsAsync(() => _combinations.ToList());
        _attributes.Setup(a => a.DeleteProductAttributeCombinationAsync(It.IsAny<ProductAttributeCombination>()))
            .Callback<ProductAttributeCombination>(c => _combinations.Remove(c)).Returns(Task.CompletedTask);
        _attributes.Setup(a => a.DeleteProductAttributeValueAsync(It.IsAny<ProductAttributeValue>()))
            .Callback<ProductAttributeValue>(v => _values.Remove(v)).Returns(Task.CompletedTask);
        _cache.Setup(c => c.ClearTouchedAsync()).Callback(() => Assert.That(Transaction.Current, Is.Null)).Returns(Task.CompletedTask);
        _events.Setup(e => e.PublishAsync()).Callback(() => Assert.That(Transaction.Current, Is.Null)).Returns(Task.CompletedTask);
        _service = new(_api.Object, _families.Object, _products.Object, _attributes.Object, _relations.Object,
            _mappings.Object, _pictures.Object, _cache.Object, _events.Object, _logs.Object);
        Begin();
    }

    private void Begin()
    {
        _service.Begin(_request);
        Observe("9945-S", 41642);
        Observe("9945-M", 41712);
    }
    private void Observe(string sku, int? combinationId, bool verified = true, bool unavailable = false, int parentId = 1794) =>
        _service.Observe(Source(sku), new AkeneoProductImportResult
        {
            NopProductId = parentId, NopParentProductId = parentId, NopProductAttributeCombinationId = combinationId,
            CombinationOptionsVerified = verified, CombinationUnavailable = unavailable,
            VariantFamilyMappingId = 29, VariantParentModelCode = "stay_wild",
            DestinationKind = AkeneoProductDestinationKind.ProductAttributeCombination,
            ActionType = SyncItemActionType.Skipped // verified unchanged-hash outcomes count too
        });
    private Task<int> Run(CancellationToken token = default) => _service.ReconcileAsync(_request, _result, token);
    private static AkeneoProductDefinition Source(string sku) => new() { Identifier = sku, Parent = "stay_wild" };
    private static ProductAttributeValue Value(int id, string name) => new() { Id = id, Name = name, ProductAttributeMappingId = 910 };
    private static string Xml(int value, int mapping = 910) => $"<Attributes><ProductAttribute ID=\"{mapping}\"><ProductAttributeValue><Value>{value}</Value></ProductAttributeValue></ProductAttribute></Attributes>";
    private static ProductAttributeCombination Combo(int id, int value, string sku = "") => new() { Id = id, ProductId = 1794, Sku = sku, AttributesXml = Xml(value) };

    [TestCase(true)]
    [TestCase(false)]
    public async Task Routed_replace_all_protects_every_destination_and_manual_selectors(bool complete)
    {
        var routing = new AkeneoCombinationRouting { ParentProductId = 1794, ProductModelCode = "stay_wild",
            AdoptExistingCombinations = true, Routes = new() { new() { Key = "route", WhenAttribute = "type", WhenValue = "standard",
                SelectorAttributeId = 40, SelectorValue = "Tree City", AxisAttribute = "year", YearAttributeId = 27 } } };
        _families.Setup(family => family.GetByIdAsync(29)).ReturnsAsync(new AkeneoFamilyMapping { Id = 29, Enabled = true,
            CombinationRoutingJson = JsonSerializer.Serialize(routing) });
        _relations.Setup(relation => relation.GetByProductAsync(1, 1794, AkeneoManagedRelationType.ProductAttributeCombination, "combination-route:29"))
            .ReturnsAsync(new List<AkeneoManagedRelation> { new() { NopRelationEntityId = 515 }, new() { NopRelationEntityId = 41642 }, new() { NopRelationEntityId = 41712 } });
        _branch = new() { Source("9945-S") };
        _service.Begin(_request);
        _service.Observe(Source("9945-S"), new AkeneoProductImportResult { NopProductId = 1794, NopParentProductId = 1794,
            VariantFamilyMappingId = 29, VariantParentModelCode = "stay_wild", CombinationOptionsVerified = complete,
            DestinationKind = AkeneoProductDestinationKind.ProductAttributeCombination, NopProductAttributeCombinationId = 41642,
            NopProductAttributeCombinationIds = new List<int> { 41642, 41712 } });
        Assert.That(await Run(), Is.EqualTo(complete ? 1 : 0));
        Assert.That(_combinations.Select(combination => combination.Id), Is.EquivalentTo(complete
            ? new[] { 517, 41642, 41712 } : new[] { 515, 517, 41642, 41712 }));
        Assert.That(_values, Has.Count.EqualTo(4));
    }

    [TestCase(AkeneoRunMode.Full)]
    [TestCase(AkeneoRunMode.Resync)]
    public async Task Replace_all_removes_legacy_combinations_then_unused_values(AkeneoRunMode mode)
    {
        _request.RunMode = mode; Begin();
        Assert.That(await Run(), Is.EqualTo(4));
        Assert.That(_combinations.Select(c => c.Id), Is.EquivalentTo(new[] { 41642, 41712 }));
        Assert.That(_values.Select(v => v.Id), Is.EquivalentTo(new[] { 3523, 3593 }));
        _pictures.Verify(p => p.ReleaseCombinationAsync(515, It.IsAny<CancellationToken>()), Times.Once);
        _relations.Verify(r => r.DeleteByRelationEntityAsync(AkeneoManagedRelationType.ProductAttributeCombination, 515), Times.Once);
        _mappings.Verify(m => m.DeleteMappingsByNopEntityAsync(NopEntityType.ProductAttributeValue, 2045), Times.Once);
        _events.Verify(e => e.Defer(_parent), Times.Once);
        _products.Verify(p => p.DeleteProductAsync(It.IsAny<Product>()), Times.Never);
        Assert.That(await Run(), Is.Zero, "Repeating a completed cleanup is idempotent.");
    }

    [TestCase(AkeneoCollectionSyncMode.Merge)]
    [TestCase(AkeneoCollectionSyncMode.ReplaceManaged)]
    [TestCase(AkeneoCollectionSyncMode.Disabled)]
    public async Task Other_modes_preserve_everything(AkeneoCollectionSyncMode mode)
    {
        _request.ProductAttributeSyncMode = mode; Begin();
        Assert.That(await Run(), Is.Zero);
        Assert.That(_api.Invocations, Is.Empty);
    }

    [TestCase("delta")][TestCase("single")][TestCase("failed")][TestCase("error")]
    [TestCase("canceled")][TestCase("truncated")][TestCase("incomplete")][TestCase("cursor")][TestCase("write-policy")]
    public async Task Ineligible_runs_never_plan_deletions(string condition)
    {
        switch (condition)
        {
            case "delta": _request.RunMode = AkeneoRunMode.Delta; break;
            case "single": _request.RunMode = AkeneoRunMode.SingleProduct; break;
            case "failed": _result.FailedCount = 1; break;
            case "error": _result.AddError("failure"); break;
            case "canceled": _result.Canceled = true; break;
            case "truncated": _result.WasTruncated = true; break;
            case "incomplete": _result.CompletedAllPages = false; break;
            case "cursor": _request.SearchAfter = "cursor"; break;
            case "write-policy": _request.UpdateExistingProducts = false; break;
        }
        Begin();
        Assert.That(await Run(), Is.Zero);
        Assert.That(_api.Invocations, Is.Empty);
    }

    [TestCase("filtered")][TestCase("held")][TestCase("unbound")][TestCase("other-parent")]
    public async Task Incomplete_or_skipped_branch_preserves_entire_parent(string condition)
    {
        _branch.Add(Source("9945-L"));
        if (condition == "held") Observe("9945-L", 517, false);
        if (condition == "unbound") Observe("9945-L", null);
        if (condition == "other-parent") Observe("9945-L", 517, parentId: 999);
        Assert.That(await Run(), Is.Zero);
        Assert.That(_combinations.Count, Is.EqualTo(4));
        Assert.That(_result.WarningCount, Is.GreaterThan(0));
    }

    [Test]
    public async Task Unrelated_combinations_preserve_their_shared_values()
    {
        _combinations.Add(new() { Id = 600, ProductId = 1794,
            AttributesXml = Xml(2045).Replace("</Attributes>", Xml(888, 911).Replace("<Attributes>", "")) });
        Assert.That(await Run(), Is.EqualTo(3));
        Assert.That(_combinations.Any(c => c.Id == 600), Is.True);
        Assert.That(_values.Any(v => v.Id == 2045), Is.True);
    }

    [Test]
    public async Task Unused_values_are_cleaned_even_without_obsolete_combinations()
    {
        _combinations.RemoveAll(c => c.Id < 1000);
        Assert.That(await Run(), Is.EqualTo(2));
    }

    [Test]
    public async Task Shared_current_axis_value_survives_removal_of_duplicate_combination()
    {
        _combinations.Add(Combo(800, 3523));
        Assert.That(await Run(), Is.EqualTo(5));
        Assert.That(_values.Any(v => v.Id == 3523), Is.True);
    }

    [TestCase("malformed")][TestCase("missing-current")][TestCase("missing-value")][TestCase("wrong-axis")]
    public async Task Invalid_destination_prevents_cleanup(string condition)
    {
        if (condition == "malformed") _combinations[0].AttributesXml = "<bad";
        if (condition == "missing-current") _combinations.RemoveAll(c => c.Id == 41642);
        if (condition == "missing-value") _values.RemoveAll(v => v.Id == 3523);
        if (condition == "wrong-axis") _combinations.Single(c => c.Id == 41642).AttributesXml = Xml(3523, 999);
        Assert.That(await Run(), Is.Zero);
        _attributes.Verify(a => a.DeleteProductAttributeCombinationAsync(It.IsAny<ProductAttributeCombination>()), Times.Never);
    }

    [Test]
    public async Task Unavailable_verified_variant_is_not_recreated_or_kept()
    {
        Observe("9945-M", null, unavailable: true);
        Assert.That(await Run(), Is.EqualTo(6));
        Assert.That(_values.Select(v => v.Id), Is.EqualTo(new[] { 3523 }));
    }

    [Test]
    public void Remote_paging_failure_cannot_delete_anything()
    {
        _api.Setup(api => api.GetProductsPageAsync(100, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("page failed"));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await Run());
        _attributes.Verify(a => a.DeleteProductAttributeCombinationAsync(It.IsAny<ProductAttributeCombination>()), Times.Never);
    }

    [Test]
    public async Task A_destination_change_after_planning_preserves_parent()
    {
        var calls = 0;
        _attributes.Setup(a => a.GetAllProductAttributeCombinationsAsync(1794)).ReturnsAsync(() =>
        {
            if (++calls == 2) _combinations.Add(Combo(900, 2045));
            return _combinations.ToList();
        });
        Assert.That(await Run(), Is.Zero);
        Assert.That(_combinations.Count, Is.EqualTo(5));
    }

    [Test]
    public async Task Deleted_parent_is_untouched()
    {
        _parent.Deleted = true;
        Assert.That(await Run(), Is.Zero);
    }

    [Test]
    public void Cancellation_prevents_cleanup()
    {
        Assert.ThrowsAsync<OperationCanceledException>(async () => await Run(new CancellationToken(true)));
        Assert.That(_combinations.Count, Is.EqualTo(4));
    }

    [TestCase("true", false, true)]
    [TestCase("false", false, true)]
    [TestCase("true", true, false)]
    [TestCase(null, false, false)]
    [TestCase("invalid", false, false)]
    public void Held_or_invalid_published_rules_do_not_authorize_cleanup(string? value, bool held, bool expected)
    {
        var family = new AkeneoFamilyMapping { Enabled = true, ManageVariantsByEnabledStatus = true,
            VariantRelationshipMode = AkeneoVariantRelationshipMode.ProductAttributeCombinations,
            CombinationAvailabilitySource = AkeneoCombinationAvailabilitySource.PublishedRule };
        var outcome = new AkeneoProductImportResult { DestinationKind = AkeneoProductDestinationKind.ProductAttributeCombination };
        var published = new AkeneoResolvedMappedValue
        {
            Mapping = new AkeneoAttributeMapping { NopTargetTypeId = (int)NopTargetType.ProductField, NopTargetKey = "Published" },
            HasValue = value != null,
            Value = value == null ? null : new AkeneoResolvedProductValue { DisplayValue = value },
            EvaluationOutcome = held ? AkeneoMappingEvaluationOutcome.SkippedByCondition : default
        };
        Assert.That(AkeneoProductBatchSyncService.CanReconcileCombinationOptions(outcome, family, new[] { published }), Is.EqualTo(expected));
    }

    [Test]
    public void A_later_parent_inventory_failure_does_not_partially_clean_earlier_parents()
    {
        _service.Observe(new AkeneoProductDefinition { Identifier = "OTHER", Parent = "other_model" }, new AkeneoProductImportResult
        {
            NopProductId = 555, NopParentProductId = 555, NopProductAttributeCombinationId = 999,
            CombinationOptionsVerified = true, VariantFamilyMappingId = 29, VariantParentModelCode = "other_model",
            DestinationKind = AkeneoProductDestinationKind.ProductAttributeCombination
        });
        _api.Setup(api => api.GetProductsPageAsync(100, It.IsAny<string>(), It.Is<string>(s => s.Contains("other_model")), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("second branch unavailable"));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await Run());
        _attributes.Verify(a => a.DeleteProductAttributeCombinationAsync(It.IsAny<ProductAttributeCombination>()), Times.Never);
        Assert.That(_values.Count, Is.EqualTo(4));
    }

    [Test]
    public async Task Every_required_axis_must_match_before_legacy_combination_can_be_removed()
    {
        _families.Setup(f => f.GetAxisMappingsAsync(29)).ReturnsAsync(new List<AkeneoFamilyVariantAxisMapping>
        {
            new() { AkeneoAttributeCode = "garment_size", NopProductAttributeId = 10, IsRequired = true },
            new() { AkeneoAttributeCode = "color", NopProductAttributeId = 11, IsRequired = true }
        });
        _attributes.Setup(a => a.GetProductAttributeMappingsByProductIdAsync(1794)).ReturnsAsync(new List<ProductAttributeMapping>
        { new() { Id = 910, ProductId = 1794, ProductAttributeId = 10 }, new() { Id = 911, ProductId = 1794, ProductAttributeId = 11 } });
        _attributes.Setup(a => a.GetProductAttributeValuesAsync(911)).ReturnsAsync(new List<ProductAttributeValue>
        { new() { Id = 4000, ProductAttributeMappingId = 911, Name = "Blue" } });
        foreach (var combination in _combinations.Where(c => c.Id > 1000))
            combination.AttributesXml = combination.AttributesXml.Replace("</Attributes>", Xml(4000, 911).Replace("<Attributes>", ""));
        Assert.That(await Run(), Is.Zero, "Legacy size-only combinations are outside the full size/color shape.");
        Assert.That(_combinations.Count, Is.EqualTo(4));
    }

    [Test]
    public async Task Empty_branch_does_not_authorize_deletion()
    {
        _branch.Clear();
        Assert.That(await Run(), Is.Zero);
    }
}
