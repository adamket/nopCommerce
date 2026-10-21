using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.TestData;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core.Domain.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

/// <summary>
/// Product tags come from attributes mapped to Product Tag, named after the Akeneo option labels
/// (never the codes), and follow the profile's collection sync mode like categories do.
/// </summary>
[TestFixture]
public class AkeneoProductTagSynchronizerTests
{
    private FakeTagStore _store = null!;
    private Mock<IAkeneoManagedRelationService> _managedRelations = null!;
    private Mock<IAkeneoApiClient> _api = null!;
    private AkeneoProductTagSynchronizer _synchronizer = null!;

    [SetUp]
    public void SetUp()
    {
        _store = new FakeTagStore();
        _managedRelations = new Mock<IAkeneoManagedRelationService>();
        _managedRelations.SetReturnsDefault(Task.FromResult<IList<AkeneoManagedRelation>>([]));
        _managedRelations.SetReturnsDefault(Task.FromResult(new AkeneoManagedRelation()));
        _api = new Mock<IAkeneoApiClient>();
        _api.Setup(client => client.GetAttributeOptionDefinitionsAsync("product_tags", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AkeneoAttributeOptionDefinition>
            {
                new() { Code = "birthday", Labels = new() { ["en_US"] = "Birthday" } },
                new() { Code = "fall_colors", Labels = new() { ["en_US"] = "Fall Colors" } },
                new() { Code = "unlabelled", Labels = new() }
            });
        _synchronizer = new AkeneoProductTagSynchronizer(_store, _managedRelations.Object, _api.Object);
    }

    [Test]
    public async Task Disabled_does_nothing()
    {
        var context = Context(AkeneoCollectionSyncMode.Disabled, Options("birthday"));

        await _synchronizer.SynchronizeAsync(context);

        Assert.That(_store.Calls, Is.Empty);
        Assert.That(context.HasChanges, Is.False);
    }

    [Test]
    public async Task Merge_assigns_tags_by_label_reusing_or_creating_them_and_keeps_other_tags()
    {
        _store.Tags.Add(new ProductTag { Id = 1, Name = "birthday" }); // matched ignoring case
        _store.Link(10, new ProductTag { Id = 9, Name = "Handmade" }); // added by hand
        var context = Context(AkeneoCollectionSyncMode.Merge, Options("birthday", "fall_colors"));

        await _synchronizer.SynchronizeAsync(context);

        Assert.Multiple(() =>
        {
            Assert.That(_store.LinkedNames(10), Is.EquivalentTo(new[] { "Handmade", "birthday", "Fall Colors" }));
            Assert.That(_store.Created, Is.EqualTo(new[] { "Fall Colors" }));
            Assert.That(context.HasChanges, Is.True);
        });
        _managedRelations.Verify(service => service.UpsertAsync(
            7, 20, 10, AkeneoManagedRelationType.ProductTag, It.IsAny<int>(), null, "birthday"), Times.Once);
        _managedRelations.Verify(service => service.UpsertAsync(
            7, 20, 10, AkeneoManagedRelationType.ProductTag, It.IsAny<int>(), null, "Fall Colors"), Times.Once);
    }

    [Test]
    public async Task A_tag_already_on_the_product_is_left_alone_whatever_its_case()
    {
        _store.Link(10, new ProductTag { Id = 3, Name = "fall colors" });
        var context = Context(AkeneoCollectionSyncMode.ReplaceAll, Options("fall_colors"));

        await _synchronizer.SynchronizeAsync(context);

        Assert.That(_store.LinkedNames(10), Is.EqualTo(new[] { "fall colors" }));
        Assert.That(context.HasChanges, Is.False);
    }

    [Test]
    public async Task An_option_with_no_label_is_not_assigned_and_holds_back_removals()
    {
        var staleManaged = _store.Link(10, new ProductTag { Id = 4, Name = "Old" });
        GivenManaged(staleManaged);
        var context = Context(AkeneoCollectionSyncMode.ReplaceManaged, Options("unlabelled", "birthday"));

        await _synchronizer.SynchronizeAsync(context);

        Assert.Multiple(() =>
        {
            Assert.That(_store.LinkedNames(10), Is.EquivalentTo(new[] { "Old", "Birthday" }));
            Assert.That(context.Result.Warnings.Any(warning => warning.Contains("no label")), Is.True);
            Assert.That(context.Result.Warnings.Any(warning => warning.Contains("removal was skipped")), Is.True);
        });
    }

    [Test]
    public async Task ReplaceManaged_removes_only_stale_tags_the_plugin_added()
    {
        var staleManaged = _store.Link(10, new ProductTag { Id = 4, Name = "Old" });
        _store.Link(10, new ProductTag { Id = 5, Name = "Handmade" });
        var relation = GivenManaged(staleManaged);
        var context = Context(AkeneoCollectionSyncMode.ReplaceManaged, Options("birthday"));

        await _synchronizer.SynchronizeAsync(context);

        Assert.That(_store.LinkedNames(10), Is.EquivalentTo(new[] { "Handmade", "Birthday" }));
        _managedRelations.Verify(service => service.DeleteAsync(relation), Times.Once);
    }

    [Test]
    public async Task ReplaceAll_removes_every_tag_akeneo_does_not_have()
    {
        _store.Link(10, new ProductTag { Id = 4, Name = "Old" });
        _store.Link(10, new ProductTag { Id = 5, Name = "Handmade" });
        var context = Context(AkeneoCollectionSyncMode.ReplaceAll, Options("birthday"));

        await _synchronizer.SynchronizeAsync(context);

        Assert.That(_store.LinkedNames(10), Is.EqualTo(new[] { "Birthday" }));
    }

    [Test]
    public async Task A_text_value_contributes_its_comma_separated_tags()
    {
        var context = Context(AkeneoCollectionSyncMode.Merge, Text("Holiday, Gifts ,, holiday"));

        await _synchronizer.SynchronizeAsync(context);

        Assert.That(_store.LinkedNames(10), Is.EquivalentTo(new[] { "Holiday", "Gifts" }));
    }

    [Test]
    public async Task Dry_run_plans_the_same_changes_without_writing()
    {
        var context = Context(AkeneoCollectionSyncMode.Merge, Options("fall_colors"));
        var model = new AkeneoProductMappingPreviewModel();

        await _synchronizer.PlanAsync(context, model);

        var operation = model.Operations.Single();
        Assert.Multiple(() =>
        {
            Assert.That(operation.Target, Is.EqualTo("Fall Colors"));
            Assert.That(operation.ChangeType, Is.EqualTo(AkeneoDryRunOperationType.Add));
            Assert.That(operation.Detail, Does.Contain("created"));
            Assert.That(_store.Created, Is.Empty);
            Assert.That(_store.LinkedNames(10), Is.Empty);
        });
    }

    private AkeneoManagedRelation GivenManaged(ProductProductTagMapping mapping)
    {
        var relation = new AkeneoManagedRelation { Id = 50 + mapping.Id, NopRelationEntityId = mapping.Id };
        _managedRelations
            .Setup(service => service.GetByProductAsync(7, 10, AkeneoManagedRelationType.ProductTag, null))
            .ReturnsAsync(new List<AkeneoManagedRelation> { relation });
        return relation;
    }

    private static AkeneoResolvedMappedValue Options(params string[] codes) => new()
    {
        Mapping = new AkeneoAttributeMapping
        {
            AkeneoAttributeCode = "product_tags",
            AkeneoAttributeTypeId = (int)AkeneoAttributeType.MultiSelect,
            NopTargetTypeId = (int)NopTargetType.ProductTag
        },
        HasValue = true,
        Value = new AkeneoResolvedProductValue
        {
            AttributeCode = "product_tags",
            SourceAttributeType = "pim_catalog_multiselect",
            RawData = JsonSerializer.SerializeToElement(codes)
        },
        ResolvedSourceDisplayName = "product_tags"
    };

    private static AkeneoResolvedMappedValue Text(string text) => new()
    {
        Mapping = new AkeneoAttributeMapping
        {
            AkeneoAttributeCode = "keywords",
            AkeneoAttributeTypeId = (int)AkeneoAttributeType.Text,
            NopTargetTypeId = (int)NopTargetType.ProductTag
        },
        HasValue = true,
        Value = new AkeneoResolvedProductValue
        {
            AttributeCode = "keywords",
            SourceAttributeType = "pim_catalog_text",
            RawData = JsonSerializer.SerializeToElement(text)
        },
        ResolvedSourceDisplayName = "keywords"
    };

    private static AkeneoProductSyncContext Context(
        AkeneoCollectionSyncMode mode,
        AkeneoResolvedMappedValue mapped)
        => new()
        {
            Source = AkeneoTestData.Product(),
            Product = new Product { Id = 10, Name = "Season's Greetings" },
            Request = new AkeneoProductImportRequest
            {
                SyncProfileId = 7,
                SyncRunRecordId = 20,
                Locale = "en_US",
                TagSyncMode = mode
            },
            Result = new AkeneoProductImportResult(),
            MappedValues = [mapped]
        };

    /// <summary>An in-memory nopCommerce tag store.</summary>
    private sealed class FakeTagStore : IAkeneoProductTagStore
    {
        private int _nextId = 100;
        public List<ProductTag> Tags { get; } = [];
        public List<ProductProductTagMapping> Mappings { get; } = [];
        public List<string> Created { get; } = [];
        public List<string> Calls { get; } = [];

        public ProductProductTagMapping Link(int productId, ProductTag tag)
        {
            if (!Tags.Contains(tag))
                Tags.Add(tag);
            var mapping = new ProductProductTagMapping { Id = _nextId++, ProductId = productId, ProductTagId = tag.Id };
            Mappings.Add(mapping);
            return mapping;
        }

        public IEnumerable<string> LinkedNames(int productId) =>
            Mappings.Where(m => m.ProductId == productId).Select(m => Tags.Single(t => t.Id == m.ProductTagId).Name);

        public Task<IList<AkeneoProductTagLink>> GetProductTagLinksAsync(int productId)
        {
            Calls.Add(nameof(GetProductTagLinksAsync));
            return Task.FromResult<IList<AkeneoProductTagLink>>(Mappings
                .Where(m => m.ProductId == productId)
                .Select(m => new AkeneoProductTagLink(m, Tags.Single(t => t.Id == m.ProductTagId)))
                .ToList());
        }

        public Task<ProductTag> FindTagByNameAsync(string name)
        {
            Calls.Add(nameof(FindTagByNameAsync));
            return Task.FromResult(Tags.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)));
        }

        public Task<ProductTag> CreateTagAsync(string name)
        {
            Created.Add(name);
            var tag = new ProductTag { Id = _nextId++, Name = name };
            Tags.Add(tag);
            return Task.FromResult(tag);
        }

        public Task<ProductProductTagMapping> AddLinkAsync(int productId, int productTagId) =>
            Task.FromResult(Link(productId, Tags.Single(t => t.Id == productTagId)));

        public Task RemoveLinkAsync(ProductProductTagMapping mapping)
        {
            Mappings.Remove(mapping);
            return Task.CompletedTask;
        }
    }
}
