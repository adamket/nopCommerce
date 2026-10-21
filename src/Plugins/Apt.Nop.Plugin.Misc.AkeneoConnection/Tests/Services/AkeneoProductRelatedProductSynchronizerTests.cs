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
/// The products a product model or standalone product becomes get nopCommerce related products
/// from one Akeneo association type, resolved through the plugin's bindings.
/// </summary>
[TestFixture]
public class AkeneoProductRelatedProductSynchronizerTests
{
    private const string Type = "related_products";

    private readonly List<RelatedProduct> _related = [];
    private readonly Dictionary<int, Product> _products = new();
    private Mock<IProductService> _productService = null!;
    private Mock<IAkeneoNopEntityMappingService> _mappings = null!;
    private Mock<IAkeneoProductSyncStateService> _syncStates = null!;
    private Mock<IAkeneoManagedRelationService> _managedRelations = null!;
    private AkeneoProductRelatedProductSynchronizer _synchronizer = null!;
    private int _nextId = 500;

    [SetUp]
    public void SetUp()
    {
        _related.Clear();
        _products.Clear();
        foreach (var (id, name) in new[] { (10, "Apple"), (20, "Granny Smith Apple"), (30, "Lodi Apple"), (40, "Pruner"), (90, "Old Link"), (91, "Manual Link") })
            _products[id] = new Product { Id = id, Name = name };

        _productService = new Mock<IProductService>();
        _productService.Setup(service => service.GetProductByIdAsync(It.IsAny<int>()))
            .ReturnsAsync((int id) => _products.GetValueOrDefault(id));
        _productService.Setup(service => service.GetRelatedProductsByProductId1Async(It.IsAny<int>(), true))
            .ReturnsAsync((int id, bool _) => _related.Where(related => related.ProductId1 == id).ToList());
        _productService.Setup(service => service.InsertRelatedProductAsync(It.IsAny<RelatedProduct>()))
            .Callback<RelatedProduct>(related => { related.Id = _nextId++; _related.Add(related); })
            .Returns(Task.CompletedTask);
        _productService.Setup(service => service.DeleteRelatedProductAsync(It.IsAny<RelatedProduct>()))
            .Callback<RelatedProduct>(related => _related.Remove(related))
            .Returns(Task.CompletedTask);

        _mappings = new Mock<IAkeneoNopEntityMappingService>();
        _mappings.SetReturnsDefault(Task.FromResult<int?>(null));
        _syncStates = new Mock<IAkeneoProductSyncStateService>();
        _syncStates.SetReturnsDefault(Task.FromResult<AkeneoProductSyncState>(null));
        _managedRelations = new Mock<IAkeneoManagedRelationService>();
        _managedRelations.SetReturnsDefault(Task.FromResult<IList<AkeneoManagedRelation>>([]));
        _managedRelations.SetReturnsDefault(Task.FromResult(new AkeneoManagedRelation()));

        _synchronizer = new AkeneoProductRelatedProductSynchronizer(
            _productService.Object,
            _mappings.Object,
            _syncStates.Object,
            _managedRelations.Object);
    }

    [Test]
    public async Task Disabled_does_nothing()
    {
        await _synchronizer.SynchronizeAsync(ModelContext(AkeneoCollectionSyncMode.Disabled, models: ["granny"]));

        _productService.Verify(service => service.InsertRelatedProductAsync(It.IsAny<RelatedProduct>()), Times.Never);
    }

    [Test]
    public async Task Merge_links_product_models_after_existing_links_and_keeps_others()
    {
        MapModel("granny", 20);
        MapModel("lodi", 30);
        Link(10, 91, displayOrder: 4);
        var context = ModelContext(AkeneoCollectionSyncMode.Merge, models: ["granny", "lodi"]);

        await _synchronizer.SynchronizeAsync(context);

        Assert.Multiple(() =>
        {
            Assert.That(RelatedTo(10), Is.EquivalentTo(new[] { 91, 20, 30 }));
            Assert.That(_related.Where(r => r.ProductId2 is 20 or 30).Select(r => r.DisplayOrder), Is.EquivalentTo(new[] { 5, 6 }));
            Assert.That(context.HasChanges, Is.True);
        });
        _managedRelations.Verify(service => service.UpsertAsync(
            7, 20, 10, AkeneoManagedRelationType.RelatedProduct, It.IsAny<int>(), null, "granny"), Times.Once);
    }

    [Test]
    public async Task Variant_leaves_are_skipped_because_their_model_carries_the_associations()
    {
        MapModel("granny", 20);
        var context = ModelContext(AkeneoCollectionSyncMode.Merge, models: ["granny"]);
        var leaf = new AkeneoProductSyncContext
        {
            Source = new AkeneoProductDefinition { Identifier = "SKU-1", Parent = "apple", Associations = context.Source.Associations },
            SourceEntityType = AkeneoEntityType.Product,
            SourceCode = "SKU-1",
            Product = _products[10],
            Request = context.Request,
            Result = new AkeneoProductImportResult()
        };

        await _synchronizer.SynchronizeAsync(leaf);

        Assert.That(RelatedTo(10), Is.Empty);
    }

    [Test]
    public async Task A_missing_association_type_changes_nothing_even_with_replace_all()
    {
        Link(10, 90);
        var context = ModelContext(AkeneoCollectionSyncMode.ReplaceAll, models: []);
        context.Source.Associations.Clear();

        await _synchronizer.SynchronizeAsync(context);

        Assert.Multiple(() =>
        {
            Assert.That(RelatedTo(10), Is.EqualTo(new[] { 90 }));
            Assert.That(context.Result.Warnings.Any(w => w.Contains("no 'related_products' association")), Is.True);
        });
    }

    [Test]
    public async Task An_unresolved_target_holds_back_removals_and_is_linked_by_the_end_of_run_retry()
    {
        MapModel("granny", 20);
        Link(10, 90);
        var context = ModelContext(AkeneoCollectionSyncMode.ReplaceAll, models: ["granny", "lodi"]);

        await _synchronizer.SynchronizeAsync(context);

        Assert.That(RelatedTo(10), Is.EquivalentTo(new[] { 90, 20 }), "No removal while lodi is unresolved.");
        Assert.That(context.Result.Warnings.Any(w => w.Contains("not in nopCommerce yet")), Is.True);

        // Lodi is synced later in the same run.
        MapModel("lodi", 30);
        var pending = _synchronizer.TakePending();
        var outcome = await _synchronizer.RetryAsync(pending.Single());

        Assert.Multiple(() =>
        {
            Assert.That(RelatedTo(10), Is.EquivalentTo(new[] { 20, 30 }));
            Assert.That(outcome, Is.EqualTo(new AkeneoRelatedProductRetryOutcome(1, 1, 0)));
            Assert.That(_synchronizer.TakePending(), Is.Empty);
        });
    }

    [Test]
    public async Task ReplaceManaged_removes_only_stale_links_the_plugin_added()
    {
        MapModel("granny", 20);
        var stale = Link(10, 90);
        Link(10, 91);
        var relation = new AkeneoManagedRelation { Id = 70, NopRelationEntityId = stale.Id };
        _managedRelations
            .Setup(service => service.GetByProductAsync(7, 10, AkeneoManagedRelationType.RelatedProduct, null))
            .ReturnsAsync(new List<AkeneoManagedRelation> { relation });

        await _synchronizer.SynchronizeAsync(ModelContext(AkeneoCollectionSyncMode.ReplaceManaged, models: ["granny"]));

        Assert.That(RelatedTo(10), Is.EquivalentTo(new[] { 91, 20 }));
        _managedRelations.Verify(service => service.DeleteAsync(relation), Times.Once);
    }

    [Test]
    public async Task Products_resolve_by_identifier_uuid_or_their_combinations_parent()
    {
        _mappings.Setup(service => service.GetMappedNopEntityIdByAkeneoUuidAsync(
                AkeneoEntityType.Product, "uuid-20", NopEntityType.Product))
            .ReturnsAsync(20);
        _mappings.Setup(service => service.GetMappedNopEntityIdByAkeneoCodeAsync(
                AkeneoEntityType.Product, "PRUNER", NopEntityType.Product))
            .ReturnsAsync(40);
        _syncStates.Setup(service => service.GetBySourceAsync(7, AkeneoEntityType.Product, "COMBO-1", "COMBO-1"))
            .ReturnsAsync(new AkeneoProductSyncState { NopProductId = 30 });

        await _synchronizer.SynchronizeAsync(ModelContext(
            AkeneoCollectionSyncMode.Merge,
            models: [],
            products: ["uuid-20", "PRUNER", "COMBO-1"]));

        Assert.That(RelatedTo(10), Is.EquivalentTo(new[] { 20, 40, 30 }));
    }

    [Test]
    public async Task A_product_is_never_related_to_itself()
    {
        MapModel("apple", 10);

        await _synchronizer.SynchronizeAsync(ModelContext(AkeneoCollectionSyncMode.Merge, models: ["apple"]));

        Assert.That(RelatedTo(10), Is.Empty);
    }

    [Test]
    public async Task Dry_run_plans_the_links_without_writing_or_queueing_a_retry()
    {
        MapModel("granny", 20);
        var model = new AkeneoProductMappingPreviewModel();

        await _synchronizer.PlanAsync(ModelContext(AkeneoCollectionSyncMode.Merge, models: ["granny", "lodi"]), model);

        Assert.Multiple(() =>
        {
            Assert.That(model.Operations.Single(o => o.ChangeType == AkeneoDryRunOperationType.Add).Target, Is.EqualTo("Granny Smith Apple"));
            Assert.That(model.Operations.Any(o => o.ChangeType == AkeneoDryRunOperationType.Review), Is.True);
            Assert.That(RelatedTo(10), Is.Empty);
            Assert.That(_synchronizer.TakePending(), Is.Empty);
        });
    }

    private void MapModel(string code, int productId) =>
        _mappings.Setup(service => service.GetMappedNopEntityIdByAkeneoCodeAsync(
                AkeneoEntityType.ProductModel, code, NopEntityType.Product))
            .ReturnsAsync(productId);

    private RelatedProduct Link(int from, int to, int displayOrder = 0)
    {
        var related = new RelatedProduct { Id = _nextId++, ProductId1 = from, ProductId2 = to, DisplayOrder = displayOrder };
        _related.Add(related);
        return related;
    }

    private IEnumerable<int> RelatedTo(int productId) =>
        _related.Where(related => related.ProductId1 == productId).Select(related => related.ProductId2);

    private AkeneoProductSyncContext ModelContext(
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
                    ["X_SELL"] = new()
                }
            },
            SourceEntityType = AkeneoEntityType.ProductModel,
            SourceCode = "apple",
            Product = _products[10],
            Request = new AkeneoProductImportRequest
            {
                SyncProfileId = 7,
                SyncRunRecordId = 20,
                RelatedProductSyncMode = mode,
                RelatedProductAssociationType = Type
            },
            Result = new AkeneoProductImportResult()
        };
}
