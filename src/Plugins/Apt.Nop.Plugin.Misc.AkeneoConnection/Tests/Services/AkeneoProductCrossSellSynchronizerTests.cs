using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

/// <summary>
/// Cross-sells are synced exactly like related products, from their own association type, into
/// nopCommerce cross-sell products.
/// </summary>
[TestFixture]
public class AkeneoProductCrossSellSynchronizerTests
{
    private const string Type = "cross_sell";

    private readonly List<CrossSellProduct> _crossSells = [];
    private readonly Dictionary<int, Product> _products = new();
    private Mock<IProductService> _productService = null!;
    private Mock<IAkeneoNopEntityMappingService> _mappings = null!;
    private Mock<IAkeneoManagedRelationService> _managedRelations = null!;
    private AkeneoProductCrossSellSynchronizer _synchronizer = null!;
    private int _nextId = 500;

    [SetUp]
    public void SetUp()
    {
        _crossSells.Clear();
        _products.Clear();
        foreach (var (id, name) in new[] { (10, "Apple"), (20, "Pruner"), (30, "Tree Wrap"), (90, "Old Link") })
            _products[id] = new Product { Id = id, Name = name };

        _productService = new Mock<IProductService>();
        _productService.Setup(service => service.GetProductByIdAsync(It.IsAny<int>()))
            .ReturnsAsync((int id) => _products.GetValueOrDefault(id));
        _productService.Setup(service => service.GetCrossSellProductsByProductId1Async(It.IsAny<int>(), true))
            .ReturnsAsync((int id, bool _) => _crossSells.Where(link => link.ProductId1 == id).ToList());
        _productService.Setup(service => service.InsertCrossSellProductAsync(It.IsAny<CrossSellProduct>()))
            .Callback<CrossSellProduct>(link => { link.Id = _nextId++; _crossSells.Add(link); })
            .Returns(Task.CompletedTask);
        _productService.Setup(service => service.DeleteCrossSellProductAsync(It.IsAny<CrossSellProduct>()))
            .Callback<CrossSellProduct>(link => _crossSells.Remove(link))
            .Returns(Task.CompletedTask);

        _mappings = new Mock<IAkeneoNopEntityMappingService>();
        _mappings.SetReturnsDefault(Task.FromResult<int?>(null));
        var syncStates = new Mock<IAkeneoProductSyncStateService>();
        syncStates.SetReturnsDefault(Task.FromResult<AkeneoProductSyncState>(null));
        _managedRelations = new Mock<IAkeneoManagedRelationService>();
        _managedRelations.SetReturnsDefault(Task.FromResult<IList<AkeneoManagedRelation>>([]));
        _managedRelations.SetReturnsDefault(Task.FromResult(new AkeneoManagedRelation()));

        _synchronizer = new AkeneoProductCrossSellSynchronizer(
            _productService.Object,
            _mappings.Object,
            syncStates.Object,
            _managedRelations.Object);
    }

    [Test]
    public async Task Merge_adds_cross_sells_and_records_them_as_managed()
    {
        MapModel("pruner", 20);
        MapProduct("WRAP-1", 30);
        var context = Context(AkeneoCollectionSyncMode.Merge, models: ["pruner"], products: ["WRAP-1"]);

        await _synchronizer.SynchronizeAsync(context);

        Assert.Multiple(() =>
        {
            Assert.That(CrossSellsOf(10), Is.EquivalentTo(new[] { 20, 30 }));
            Assert.That(context.HasChanges, Is.True);
        });
        _managedRelations.Verify(service => service.UpsertAsync(
            7, 20, 10, AkeneoManagedRelationType.CrossSellProduct, It.IsAny<int>(), null, "pruner"), Times.Once);
        _productService.Verify(service => service.InsertRelatedProductAsync(It.IsAny<RelatedProduct>()), Times.Never);
    }

    [Test]
    public async Task Disabled_does_nothing_even_when_related_products_are_on()
    {
        MapModel("pruner", 20);
        var context = Context(AkeneoCollectionSyncMode.Disabled, models: ["pruner"]);
        context.Request.RelatedProductSyncMode = AkeneoCollectionSyncMode.Merge;
        context.Request.RelatedProductAssociationType = "related_products";

        await _synchronizer.SynchronizeAsync(context);

        Assert.That(CrossSellsOf(10), Is.Empty);
    }

    [Test]
    public async Task Replace_managed_removes_only_stale_links_the_plugin_owns()
    {
        MapModel("pruner", 20);
        var stale = Link(10, 90);
        Link(10, 30); // added by hand in nopCommerce
        _managedRelations.Setup(service => service.GetByProductAsync(7, 10, AkeneoManagedRelationType.CrossSellProduct, null))
            .ReturnsAsync([new AkeneoManagedRelation { NopRelationEntityId = stale.Id, AkeneoValueCode = "old" }]);

        await _synchronizer.SynchronizeAsync(Context(AkeneoCollectionSyncMode.ReplaceManaged, models: ["pruner"]));

        Assert.That(CrossSellsOf(10), Is.EquivalentTo(new[] { 30, 20 }));
    }

    [Test]
    public async Task Missing_association_type_in_Akeneo_is_reviewed_and_changes_nothing()
    {
        Link(10, 90);
        var context = Context(AkeneoCollectionSyncMode.ReplaceAll, models: []);
        context.Request.CrossSellAssociationType = "X_SELL_TYPO";

        await _synchronizer.SynchronizeAsync(context);

        Assert.Multiple(() =>
        {
            Assert.That(CrossSellsOf(10), Is.EquivalentTo(new[] { 90 }));
            Assert.That(context.Result.Warnings.Any(w => w.Contains("no 'X_SELL_TYPO' association") &&
                                                         w.Contains("cross-sell products were left unchanged")), Is.True);
        });
    }

    [Test]
    public async Task Unresolved_target_is_retried_once_it_is_synced()
    {
        var context = Context(AkeneoCollectionSyncMode.Merge, models: ["pruner"]);

        await _synchronizer.SynchronizeAsync(context);
        Assert.That(context.Result.Warnings.Any(w => w.StartsWith("Cross-sell product model pruner is not in nopCommerce yet")), Is.True);

        MapModel("pruner", 20);
        var work = _synchronizer.TakePending().Single();
        var outcome = await _synchronizer.RetryAsync(work);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(new AkeneoRelatedProductRetryOutcome(1, 0, 0)));
            Assert.That(CrossSellsOf(10), Is.EquivalentTo(new[] { 20 }));
        });
    }

    [Test]
    public async Task Dry_run_shows_the_cross_sell_area()
    {
        MapModel("pruner", 20);
        var model = new AkeneoProductMappingPreviewModel();

        await _synchronizer.PlanAsync(Context(AkeneoCollectionSyncMode.Merge, models: ["pruner"]), model);

        var add = model.Operations.Single(o => o.ChangeType == AkeneoDryRunOperationType.Add);
        Assert.Multiple(() =>
        {
            Assert.That(add.Area, Is.EqualTo("Cross-sell products"));
            Assert.That(add.ProposedValue, Is.EqualTo("Cross-sell"));
            Assert.That(CrossSellsOf(10), Is.Empty);
        });
    }

    private void MapModel(string code, int productId) =>
        _mappings.Setup(service => service.GetMappedNopEntityIdByAkeneoCodeAsync(
                AkeneoEntityType.ProductModel, code, NopEntityType.Product))
            .ReturnsAsync(productId);

    private void MapProduct(string identifier, int productId) =>
        _mappings.Setup(service => service.GetMappedNopEntityIdByAkeneoCodeAsync(
                AkeneoEntityType.Product, identifier, NopEntityType.Product))
            .ReturnsAsync(productId);

    private CrossSellProduct Link(int from, int to)
    {
        var link = new CrossSellProduct { Id = _nextId++, ProductId1 = from, ProductId2 = to };
        _crossSells.Add(link);
        return link;
    }

    private IEnumerable<int> CrossSellsOf(int productId) =>
        _crossSells.Where(link => link.ProductId1 == productId).Select(link => link.ProductId2);

    private AkeneoProductSyncContext Context(
        AkeneoCollectionSyncMode mode,
        string[] models,
        string[]? products = null) => new()
        {
            Source = new AkeneoProductDefinition
            {
                Code = "apple",
                Associations = new Dictionary<string, AkeneoAssociationDefinition>
                {
                    [Type] = new() { ProductModels = models.ToList(), Products = (products ?? []).ToList() },
                    ["related_products"] = new() { ProductModels = ["pruner"] }
                }
            },
            SourceEntityType = AkeneoEntityType.ProductModel,
            SourceCode = "apple",
            Product = _products[10],
            Request = new AkeneoProductImportRequest
            {
                SyncProfileId = 7,
                SyncRunRecordId = 20,
                CrossSellSyncMode = mode,
                CrossSellAssociationType = Type
            },
            Result = new AkeneoProductImportResult()
        };
}
