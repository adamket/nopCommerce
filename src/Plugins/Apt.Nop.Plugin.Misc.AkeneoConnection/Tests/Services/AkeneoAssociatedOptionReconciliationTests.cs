using System.Text.Json;
using System.Transactions;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

[TestFixture]
public class AkeneoAssociatedOptionReconciliationTests
{
    private Mock<IAkeneoApiClient> _api = null!;
    private Mock<IProductService> _products = null!;
    private Mock<IProductAttributeService> _attributes = null!;
    private Mock<IAkeneoManagedRelationService> _relations = null!;
    private Mock<IAkeneoAttributeValueCacheTracker> _cache = null!;
    private Mock<IAkeneoDeferredProductEvents> _events = null!;
    private Mock<IAkeneoSyncItemLogService> _logs = null!;
    private AkeneoAssociatedOptionReconciliationService _service = null!;
    private AkeneoProductBatchImportRequest _request = null!;
    private AkeneoProductBatchImportResult _result = null!;
    private List<ProductAttributeValue> _options = null!;
    private List<ProductAttributeCombination> _combinations = null!;
    private Dictionary<string, Product> _parents = null!;
    private Dictionary<string, IList<AkeneoProductDefinition>> _leaves = null!;
    private Dictionary<string, IList<AkeneoProductDefinition>> _models = null!;

    [SetUp]
    public void SetUp()
    {
        _api = new(); _products = new(); _attributes = new(); _relations = new();
        _cache = new(); _events = new(); _logs = new();
        _request = new AkeneoProductBatchImportRequest
        {
            RunMode = AkeneoRunMode.Resync, SyncProfileId = 1, SyncRunRecordId = 10,
            ProductAttributeSyncMode = AkeneoCollectionSyncMode.ReplaceAll,
            MissingProductBehavior = AkeneoMissingProductBehavior.Ignore
        };
        _result = new AkeneoProductBatchImportResult { CompletedAllPages = true };
        _parents = new() { ["arborvitae"] = new Product { Id = 159 } };
        _options = new()
        {
            Option(1165, 1040, "Plugged"), Option(1956, 1729, "Plug (10-Pack)"),
            new ProductAttributeValue { Id = 999, ProductAttributeMappingId = 100, Name = "Simple", AttributeValueTypeId = 0 }
        };
        _combinations = new();
        _leaves = new() { ["arborvitae"] = new List<AkeneoProductDefinition> { Source("9905") } };
        _models = new();
        _api.Setup(api => api.GetProductsPageAsync(100, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int _, string cursor, string search, CancellationToken token) =>
                new AkeneoProductPageResult { Items = _leaves.GetValueOrDefault(Parent(search)) ?? new List<AkeneoProductDefinition>() });
        _api.Setup(api => api.GetProductModelsPageAsync(100, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int _, string cursor, string search, CancellationToken token) =>
                new AkeneoProductPageResult { Items = _models.GetValueOrDefault(Parent(search)) ?? new List<AkeneoProductDefinition>() });
        _products.Setup(service => service.GetProductByIdAsync(159)).ReturnsAsync(_parents["arborvitae"]);
        _attributes.Setup(service => service.GetProductAttributeValueByIdAsync(It.IsAny<int>()))
            .ReturnsAsync((int id) => _options.FirstOrDefault(option => option.Id == id)!);
        _attributes.Setup(service => service.GetProductAttributeMappingByIdAsync(100))
            .ReturnsAsync(new ProductAttributeMapping { Id = 100, ProductId = 159 });
        _attributes.Setup(service => service.GetProductAttributeValuesAsync(100)).ReturnsAsync(() => _options.ToList());
        _attributes.Setup(service => service.GetAllProductAttributeCombinationsAsync(159)).ReturnsAsync(_combinations);
        _attributes.Setup(service => service.DeleteProductAttributeValueAsync(It.IsAny<ProductAttributeValue>()))
            .Callback<ProductAttributeValue>(option => _options.Remove(option)).Returns(Task.CompletedTask);
        _cache.Setup(cache => cache.ClearTouchedAsync()).Callback(() => Assert.That(Transaction.Current, Is.Null))
            .Returns(Task.CompletedTask);
        _events.Setup(events => events.PublishAsync()).Callback(() => Assert.That(Transaction.Current, Is.Null))
            .Returns(Task.CompletedTask);
        _service = new(_api.Object, _products.Object, _attributes.Object, _relations.Object,
            _cache.Object, _events.Object, _logs.Object);
        Begin();
    }

    private void Begin()
    {
        _service.Begin(_request);
        Observe("9905", 1729, 1956, true);
    }

    private void Observe(string uuid, int productId, int? valueId, bool verified) =>
        _service.Observe(Source(uuid), new AkeneoProductImportResult
        {
            NopProductId = productId, NopParentProductId = 159,
            NopProductAttributeValueId = valueId, AssociatedOptionsVerified = verified,
            DestinationKind = AkeneoProductDestinationKind.AssociatedProduct,
            ActionType = SyncItemActionType.Skipped // includes the unchanged-hash path
        });

    private Task<int> Run() => _service.ReconcileAsync(_request, _result, _parents);
    private static ProductAttributeValue Option(int id, int productId, string name) => new()
    {
        Id = id, ProductAttributeMappingId = 100, Name = name,
        AssociatedProductId = productId, AttributeValueTypeId = (int)AttributeValueType.AssociatedToProduct
    };
    private static AkeneoProductDefinition Source(string uuid, string parent = "arborvitae") => new() { Uuid = uuid, Parent = parent };
    private static string Parent(string search) => JsonDocument.Parse(search).RootElement.GetProperty("parent")[0].GetProperty("value")[0].GetString()!;

    [TestCase(AkeneoRunMode.Resync)]
    [TestCase(AkeneoRunMode.Full)]
    public async Task Complete_replace_all_removes_legacy_options_but_never_the_child_product(AkeneoRunMode mode)
    {
        _request.RunMode = mode;
        Begin();
        Assert.That(await Run(), Is.EqualTo(1));
        Assert.That(_options.Select(option => option.Id), Is.EquivalentTo(new[] { 1956, 999 }));
        _products.Verify(service => service.DeleteProductAsync(It.IsAny<Product>()), Times.Never);
        _products.Verify(service => service.UpdateProductAsync(It.IsAny<Product>()), Times.Never);
        _relations.Verify(service => service.DeleteByRelationEntityAsync(AkeneoManagedRelationType.AssociatedProductAttributeValue, 1165), Times.Once);
        _logs.Verify(service => service.InsertAkeneoSyncItemLogAsync(It.Is<AkeneoSyncItemLog>(log => log.Message.Contains("Plugged") && log.NopProductId == 159)), Times.Once);
        _events.Verify(service => service.Defer(_parents["arborvitae"]), Times.Once);
    }

    [Test]
    public async Task Cleanup_reports_each_grouped_product_checked_and_each_attribute_cleaned()
    {
        var reports = new List<string>();
        _request.Steps = new AkeneoSyncSteps((step, unit, done, total) => reports.Add($"{step}: {done} of {total} {unit}"));

        await Run();

        Assert.That(reports, Is.EqualTo(new[]
        {
            "Checking associated options against Akeneo: 0 of 1 grouped products",
            "Checking associated options against Akeneo: 1 of 1 grouped products",
            "Removing obsolete associated options: 0 of 1 product attributes",
            "Removing obsolete associated options: 1 of 1 product attributes"
        }));
    }

    [TestCase(AkeneoRunMode.Delta)]
    [TestCase(AkeneoRunMode.SingleProduct)]
    public async Task Partial_run_modes_do_not_even_fetch_an_inventory(AkeneoRunMode mode)
    {
        _request.RunMode = mode; Begin();
        Assert.That(await Run(), Is.Zero);
        Assert.That(_api.Invocations, Is.Empty);
    }

    [TestCase(AkeneoCollectionSyncMode.Disabled)]
    [TestCase(AkeneoCollectionSyncMode.Merge)]
    [TestCase(AkeneoCollectionSyncMode.ReplaceManaged)]
    public async Task Other_collection_modes_preserve_legacy_options(AkeneoCollectionSyncMode mode)
    {
        _request.ProductAttributeSyncMode = mode; Begin();
        Assert.That(await Run(), Is.Zero);
        Assert.That(_api.Invocations, Is.Empty);
    }

    [TestCase("failed")]
    [TestCase("error")]
    [TestCase("canceled")]
    [TestCase("truncated")]
    [TestCase("incomplete")]
    [TestCase("cursor")]
    [TestCase("write-policy")]
    public async Task Unsafe_runs_cannot_remove_options(string condition)
    {
        switch (condition)
        {
            case "failed": _result.FailedCount = 1; break;
            case "error": _result.AddError("API failure"); break;
            case "canceled": _result.Canceled = true; break;
            case "truncated": _result.WasTruncated = true; break;
            case "incomplete": _result.CompletedAllPages = false; break;
            case "cursor": _request.SearchAfter = "resume"; break;
            case "write-policy": _request.UpdateExistingProducts = false; break;
        }
        Begin();
        Assert.That(await Run(), Is.Zero);
        Assert.That(_api.Invocations, Is.Empty);
    }

    [Test]
    public async Task Held_tracked_variant_keeps_its_option()
    {
        _leaves["arborvitae"].Add(Source("1084"));
        Observe("1084", 1040, 1165, false);
        Assert.That(await Run(), Is.Zero);
    }

    [Test]
    public async Task An_unbound_skip_preserves_the_entire_parent()
    {
        _leaves["arborvitae"].Add(Source("1084"));
        Observe("1084", 0, null, false);
        Assert.That(await Run(), Is.Zero);
        Assert.That(_result.ItemWarnings.Single(), Does.Contain("skipped without an existing binding"));
    }

    [Test]
    public async Task All_held_variants_do_not_authorize_cleanup()
    {
        _service.Begin(_request);
        Observe("9905", 1729, 1956, false);
        Assert.That(await Run(), Is.Zero);
        Assert.That(_api.Invocations, Is.Empty);
    }

    [Test]
    public async Task Profile_filtered_siblings_prevent_cleanup()
    {
        _leaves["arborvitae"].Add(Source("filtered-out"));
        Assert.That(await Run(), Is.Zero);
        Assert.That(_result.WarningCount, Is.EqualTo(1));
    }

    [Test]
    public async Task Nested_models_are_inventoried_without_profile_filters()
    {
        _models["arborvitae"] = new List<AkeneoProductDefinition> { new() { Code = "plug", Parent = "arborvitae" } };
        _leaves["arborvitae"].Clear();
        _leaves["plug"] = new List<AkeneoProductDefinition> { Source("9905", "plug") };
        _request.SearchJson = "{\"enabled\":[{\"operator\":\"=\",\"value\":true}]}";
        Assert.That(await Run(), Is.EqualTo(1));
        _api.Verify(api => api.GetProductsPageAsync(100, null, It.Is<string>(search => search.Contains("plug") && !search.Contains("enabled")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task A_sibling_on_a_later_page_is_not_mistaken_for_an_absent_variant()
    {
        _api.Setup(api => api.GetProductsPageAsync(100, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int _, string cursor, string search, CancellationToken token) => cursor == null
                ? new AkeneoProductPageResult { Items = new List<AkeneoProductDefinition> { Source("9905") }, SearchAfter = "next" }
                : new AkeneoProductPageResult { Items = new List<AkeneoProductDefinition> { Source("1084") } });
        Observe("1084", 1040, 1165, false);
        Assert.That(await Run(), Is.Zero);
    }

    [TestCase("null")]
    [TestCase("exception")]
    [TestCase("repeated-cursor")]
    public void Failed_inventory_never_deletes_an_option(string failure)
    {
        var setup = _api.Setup(api => api.GetProductsPageAsync(100, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()));
        if (failure == "exception") setup.ThrowsAsync(new InvalidOperationException("API failure"));
        else if (failure == "null") setup.ReturnsAsync((AkeneoProductPageResult)null!);
        else setup.ReturnsAsync(new AkeneoProductPageResult { Items = new List<AkeneoProductDefinition> { Source("9905") }, SearchAfter = "same" });
        Assert.ThrowsAsync<InvalidOperationException>(async () => await Run());
        _attributes.Verify(service => service.DeleteProductAttributeValueAsync(It.IsAny<ProductAttributeValue>()), Times.Never);
    }

    [Test]
    public async Task Values_used_by_combinations_and_deleted_parents_are_preserved()
    {
        _combinations.Add(new ProductAttributeCombination { AttributesXml = "<Attributes><ProductAttribute ID=\"100\"><ProductAttributeValue><Value>1165</Value></ProductAttributeValue></ProductAttribute></Attributes>" });
        Assert.That(await Run(), Is.Zero);
        _combinations.Clear();
        _parents["arborvitae"].Deleted = true;
        Assert.That(await Run(), Is.Zero);
    }

    [Test]
    public async Task Beginning_another_run_discards_previous_observations()
    {
        _service.Begin(_request);
        Assert.That(await Run(), Is.Zero);
    }

    [Test]
    public async Task A_held_child_is_protected_even_if_its_saved_option_id_is_missing()
    {
        _leaves["arborvitae"].Add(Source("1084"));
        Observe("1084", 1040, null, false);
        Assert.That(await Run(), Is.Zero);
    }

    [Test]
    public void All_parent_inventories_finish_before_any_deletion()
    {
        _parents["second"] = new Product { Id = 200 };
        _service.Observe(Source("second-leaf", "second"), new AkeneoProductImportResult
        {
            NopProductId = 201, NopParentProductId = 200,
            NopProductAttributeValueId = 2000, AssociatedOptionsVerified = true
        });
        _api.Setup(api => api.GetProductsPageAsync(100, null,
                It.Is<string>(search => search.Contains("second")), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("second inventory failed"));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await Run());
        _attributes.Verify(service => service.DeleteProductAttributeValueAsync(It.IsAny<ProductAttributeValue>()), Times.Never);
    }

    [Test]
    public void Losing_the_sync_lease_prevents_deletion()
    {
        Assert.ThrowsAsync<AkeneoSyncLeaseLostException>(async () =>
            await _service.ReconcileAsync(_request, _result, _parents, renewLease:
                () => throw new AkeneoSyncLeaseLostException()));
        _attributes.Verify(service => service.DeleteProductAttributeValueAsync(It.IsAny<ProductAttributeValue>()), Times.Never);
    }

    [Test]
    public void A_failed_removal_never_publishes_a_product_event()
    {
        _logs.Setup(logs => logs.InsertAkeneoSyncItemLogAsync(It.IsAny<AkeneoSyncItemLog>()))
            .ThrowsAsync(new InvalidOperationException("database write failed"));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await Run());
        _events.Verify(events => events.Defer(It.IsAny<Product>()), Times.Never);
        _cache.Verify(cache => cache.ClearTouchedAsync(), Times.Once);
    }
}
