using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Moq;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;
using NUnit.Framework;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

[TestFixture]
public class AkeneoCombinationRoutingTests
{
    private sealed class Fixture
    {
        public readonly List<ProductAttributeMapping> Mappings = new();
        public readonly List<ProductAttributeValue> Values = new();
        public readonly List<ProductAttributeCombination> Combinations = new();
        public readonly List<AkeneoManagedRelation> Owned = new();
        public readonly Mock<IProductAttributeService> Attributes = new();
        public readonly Mock<IAkeneoManagedRelationService> Relations = new();
        public readonly Mock<IAkeneoCombinationPictureSynchronizer> Pictures = new();
        public readonly AkeneoCombinationRouting Configuration = new()
        {
            ProductModelCode = "plaque_emblems", ParentProductId = 2093, AdoptExistingCombinations = true
        };
        public readonly Product Parent = new() { Id = 2093 };
        public readonly AkeneoCombinationRoutingService Service;
        private int nextId = 50000;
        public Fixture()
        {
            Mappings.Add(new() { Id = 1045, ProductId = 2093, ProductAttributeId = 40 });
            AddRoute("line", "Tree Line", "tree_line_usa", 36, 1048, 2778);
            AddRoute("city", "Tree City", "tree_city_usa", 27, 1047, 2779);
            AddRoute("campus", "Tree Campus", "tree_campus", 30, 1046, 2780);
            AddRoute("growth", "Growth Award", null, 37, 1049, 2781);
            Attributes.Setup(service => service.GetProductAttributeMappingsByProductIdAsync(2093)).ReturnsAsync(Mappings);
            Attributes.Setup(service => service.GetProductAttributeValuesAsync(It.IsAny<int>()))
                .ReturnsAsync((int mapping) => Values.Where(value => value.ProductAttributeMappingId == mapping).ToList());
            Attributes.Setup(service => service.GetAllProductAttributeCombinationsAsync(2093)).ReturnsAsync(() => Combinations.ToList());
            Attributes.Setup(service => service.InsertProductAttributeValueAsync(It.IsAny<ProductAttributeValue>()))
                .Callback<ProductAttributeValue>(value => { value.Id = nextId++; Values.Add(value); }).Returns(Task.CompletedTask);
            Attributes.Setup(service => service.InsertProductAttributeCombinationAsync(It.IsAny<ProductAttributeCombination>()))
                .Callback<ProductAttributeCombination>(combination => { combination.Id = nextId++; Combinations.Add(combination); }).Returns(Task.CompletedTask);
            Attributes.Setup(service => service.DeleteProductAttributeCombinationAsync(It.IsAny<ProductAttributeCombination>()))
                .Callback<ProductAttributeCombination>(combination => Combinations.Remove(combination)).Returns(Task.CompletedTask);
            Relations.Setup(service => service.GetByProductAsync(7, 2093, AkeneoManagedRelationType.ProductAttributeCombination, "combination-route:3"))
                .ReturnsAsync(() => Owned.ToList());
            Relations.Setup(service => service.UpsertAsync(7, It.IsAny<int>(), 2093, AkeneoManagedRelationType.ProductAttributeCombination,
                    It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((int profile, int run, int parent, AkeneoManagedRelationType kind, int id, string attribute, string code) =>
                {
                    Owned.RemoveAll(relation => relation.NopRelationEntityId == id);
                    var relation = new AkeneoManagedRelation { NopRelationEntityId = id, AkeneoAttributeCode = attribute, AkeneoValueCode = code };
                    Owned.Add(relation); return relation;
                });
            Relations.Setup(service => service.DeleteByRelationEntityAsync(AkeneoManagedRelationType.ProductAttributeCombination, It.IsAny<int>()))
                .Callback<AkeneoManagedRelationType,int>((kind, id) => Owned.RemoveAll(relation => relation.NopRelationEntityId == id)).Returns(Task.CompletedTask);
            var availability = new Mock<IAkeneoCombinationAvailabilityService>();
            availability.Setup(service => service.IsAvailableAsync(It.IsAny<AkeneoVariantImportContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((AkeneoVariantImportContext context, CancellationToken token) =>
                {
                    if (context.PublishedRuleProblem != null) throw new NopException(context.PublishedRuleProblem);
                    return !context.ManageVariantsByEnabledStatus || context.PublishedRuleResult == true;
                });
            Service = new(Attributes.Object, Relations.Object, new AkeneoProductValueResolver(), availability.Object,
                Pictures.Object, Mock.Of<IAkeneoNopEntityMappingService>(), Mock.Of<IAkeneoAttributeValueCacheTracker>());
        }
        private void AddRoute(string key, string selector, string? program, int attributeId, int mappingId, int selectorId)
        {
            Values.Add(new() { Id = selectorId, ProductAttributeMappingId = 1045, Name = selector });
            Mappings.Add(new() { Id = mappingId, ProductId = 2093, ProductAttributeId = attributeId,
                ConditionAttributeXml = AkeneoCombinationRoutingService.SelectionXml((1045, selectorId)) });
            Configuration.Routes.Add(new() { Key = key, WhenAttribute = "emblem_type", WhenValue = program == null ? "growth_award" : "standard",
                MembershipAttribute = program == null ? null : "programs", MembershipValue = program,
                SelectorAttributeId = 40, SelectorValue = selector, AxisAttribute = "award_year", YearAttributeId = attributeId });
        }
        public AkeneoVariantImportContext Context(int year = 2025, string type = "standard", string[]? programs = null, string sku = "SKU") => new()
        {
            AkeneoUuid = "uuid-" + sku, AkeneoIdentifier = sku, Sku = sku, SyncProfileId = 7,
            ManageVariantsByEnabledStatus = true, CombinationAvailabilitySource = AkeneoCombinationAvailabilitySource.PublishedRule,
            PublishedRuleResult = true, Price = 12.50m, StockQuantity = 8,
            Options = new() { FamilyVariantImportConfigurationId = 3 },
            SourceProduct = new AkeneoProductDefinition { Parent = "plaque_emblems", Values = JsonSerializer.SerializeToElement(new
            {
                emblem_type = new[] { new { locale = (string?)null, scope = (string?)null, data = type } },
                award_year = new[] { new { locale = (string?)null, scope = (string?)null, data = year.ToString() } },
                programs = new[] { new { locale = (string?)null, scope = (string?)null, data = programs ?? new[] { "tree_city_usa", "tree_line_usa", "tree_campus" } } }
            }) }
        };
        public Task<AkeneoVariantSyncResult> Apply(AkeneoVariantImportContext context) => Service.ApplyAsync(Parent, context,
            AkeneoVariantRelationshipSource.AkeneoFamilyConfiguration, Configuration);
    }

    [Test]
    public async Task StandardVariantFansOutAndRepeatedSyncKeepsCombinationIds()
    {
        var fixture = new Fixture(); var context = fixture.Context();
        var first = await fixture.Apply(context); var ids = first.NopProductAttributeCombinationIds.ToArray();
        var second = await fixture.Apply(context);
        Assert.Multiple(() =>
        {
            Assert.That(ids, Has.Length.EqualTo(3));
            Assert.That(second.NopProductAttributeCombinationIds, Is.EquivalentTo(ids));
            Assert.That(fixture.Combinations, Has.Count.EqualTo(3));
            Assert.That(fixture.Combinations.All(combination => combination.Sku == "SKU" && combination.OverriddenPrice == 12.5m && combination.StockQuantity == 8), Is.True);
            Assert.That(second.Changed, Is.False);
        });
    }

    [Test]
    public async Task PreviewCatalogShapeProduces132CombinationsFrom82Variants()
    {
        var fixture = new Fixture();
        for (var year = 1979; year <= 2026; year++)
        {
            var programs = new List<string>();
            if (year <= 2025) programs.Add("tree_city_usa");
            if (year >= 1994) programs.Add("tree_line_usa");
            if (year >= 2008 && year <= 2025) programs.Add("tree_campus");
            await fixture.Apply(fixture.Context(year, programs: programs.ToArray(), sku: "standard-" + year));
        }
        for (var year = 1991; year <= 2024; year++)
            await fixture.Apply(fixture.Context(year, "growth_award", Array.Empty<string>(), "growth-" + year));
        Assert.That(fixture.Combinations, Has.Count.EqualTo(132));
        Assert.That(fixture.Combinations.Select(combination => combination.Sku).Distinct().Count(), Is.EqualTo(82));
    }

    [Test]
    public async Task ExistingCombinationsAreAdoptedWithoutDuplicatingOrChangingSelectors()
    {
        var fixture = new Fixture(); var context = fixture.Context();
        await fixture.Apply(context); var ids = fixture.Combinations.Select(combination => combination.Id).ToArray();
        fixture.Owned.Clear();
        var result = await fixture.Apply(context);
        Assert.That(result.NopProductAttributeCombinationIds, Is.EquivalentTo(ids));
        Assert.That(fixture.Owned, Has.Count.EqualTo(3));
        fixture.Attributes.Verify(service => service.UpdateProductAttributeMappingAsync(It.IsAny<ProductAttributeMapping>()), Times.Never);
    }

    [Test]
    public async Task MembershipRemovalAndUnpublicationRemoveOnlyOwnedCombinations()
    {
        var fixture = new Fixture(); await fixture.Apply(fixture.Context());
        var unrelated = new ProductAttributeCombination { Id = 90000, ProductId = 2093, Sku = "manual", AttributesXml = "<Attributes/>" };
        fixture.Combinations.Add(unrelated); var countValues = fixture.Values.Count;
        var context = fixture.Context(programs: new[] { "tree_line_usa" });
        var result = await fixture.Apply(context);
        Assert.That(result.NopProductAttributeCombinationIds, Has.Count.EqualTo(1));
        Assert.That(fixture.Combinations, Has.Count.EqualTo(2));
        context.PublishedRuleResult = false; await fixture.Apply(context);
        Assert.That(fixture.Combinations, Is.EqualTo(new[] { unrelated }));
        Assert.That(fixture.Values.Count, Is.EqualTo(countValues));
    }

    [Test]
    public async Task HeldPublishedRulePreservesAllRoutesEvenWhenMembershipChanges()
    {
        var fixture = new Fixture(); await fixture.Apply(fixture.Context());
        var context = fixture.Context(programs: Array.Empty<string>()); context.PublishedRuleHeld = true;
        var result = await fixture.Apply(context);
        Assert.That(result.NopProductAttributeCombinationIds, Has.Count.EqualTo(3));
        Assert.That(result.Changed, Is.False);
    }

    [Test]
    public async Task MissingMembershipFailsBeforeChangingAnything()
    {
        var fixture = new Fixture(); await fixture.Apply(fixture.Context());
        var context = fixture.Context(); context.SourceProduct.Values = JsonSerializer.SerializeToElement(new
        { emblem_type = new[] { new { data = "standard", locale = (string?)null, scope = (string?)null } } });
        Assert.ThrowsAsync<NopException>(() => fixture.Apply(context));
        Assert.That(fixture.Combinations, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task EmptyMembershipRemovesAllManagedStandardRoutes()
    {
        var fixture = new Fixture(); await fixture.Apply(fixture.Context());
        await fixture.Apply(fixture.Context(programs: Array.Empty<string>()));
        Assert.That(fixture.Combinations, Is.Empty);
    }

    [Test]
    public async Task DuplicateOrWrongSkuSelectionsBlockAdoption()
    {
        var fixture = new Fixture(); await fixture.Apply(fixture.Context()); fixture.Owned.Clear();
        fixture.Combinations[2].Sku = "another-sku";
        Assert.ThrowsAsync<NopException>(() => fixture.Apply(fixture.Context()));
        Assert.That(fixture.Owned, Is.Empty);
        fixture.Combinations[2].Sku = "SKU";
        fixture.Combinations.Add(new ProductAttributeCombination { Id = 99999, ProductId = 2093, Sku = "SKU", AttributesXml = fixture.Combinations[0].AttributesXml });
        Assert.ThrowsAsync<NopException>(() => fixture.Apply(fixture.Context()));
        Assert.That(fixture.Owned, Is.Empty);
    }

    [Test]
    public async Task AdoptionRequiresOptIn()
    {
        var fixture = new Fixture(); await fixture.Apply(fixture.Context()); fixture.Owned.Clear();
        fixture.Configuration.AdoptExistingCombinations = false;
        Assert.ThrowsAsync<NopException>(() => fixture.Apply(fixture.Context()));
        Assert.That(fixture.Owned, Is.Empty);
    }

    [Test]
    public void ConflictingSelectorConditionAndWrongParentFailBeforeWrites()
    {
        var fixture = new Fixture(); fixture.Mappings[1].ConditionAttributeXml = AkeneoCombinationRoutingService.SelectionXml((1045, 9999));
        Assert.ThrowsAsync<NopException>(() => fixture.Apply(fixture.Context()));
        Assert.That(fixture.Combinations, Is.Empty);
        fixture.Parent.Id = 15;
        Assert.ThrowsAsync<NopException>(() => fixture.Apply(fixture.Context()));
    }

    [Test]
    public async Task UnpublishedFirstSyncAdoptsAndRemovesAllExactMatches()
    {
        var fixture = new Fixture(); await fixture.Apply(fixture.Context()); fixture.Owned.Clear();
        var context = fixture.Context(); context.PublishedRuleResult = false;
        var result = await fixture.Apply(context);
        Assert.That(result.CombinationUnavailable, Is.True);
        Assert.That(fixture.Combinations, Is.Empty);
        Assert.That(fixture.Values, Has.Count.EqualTo(7));
    }

    [Test]
    public async Task ReconciliationOffRetainsOldRoutesButPublicationStillRemovesThem()
    {
        var fixture = new Fixture(); await fixture.Apply(fixture.Context());
        fixture.Configuration.ReconcileManagedCombinations = false;
        var context = fixture.Context(programs: new[] { "tree_line_usa" });
        var result = await fixture.Apply(context);
        Assert.That(result.NopProductAttributeCombinationIds, Has.Count.EqualTo(3));
        context.PublishedRuleResult = false; await fixture.Apply(context);
        Assert.That(fixture.Combinations, Is.Empty);
    }

    [Test]
    public async Task SkuRenameAndPriceUpdatesReachEveryOwnedRoute()
    {
        var fixture = new Fixture(); var context = fixture.Context(); await fixture.Apply(context);
        var ids = fixture.Combinations.Select(combination => combination.Id).ToArray();
        context.Sku = "new-sku"; context.Price = 19; context.StockQuantity = 4;
        await fixture.Apply(context);
        Assert.That(fixture.Combinations.Select(combination => combination.Id), Is.EquivalentTo(ids));
        Assert.That(fixture.Combinations.All(combination => combination.Sku == "new-sku" && combination.OverriddenPrice == 19 && combination.StockQuantity == 4), Is.True);
    }

    [Test]
    public async Task MissingPublishedMappingBlocksExistingRoutes()
    {
        var fixture = new Fixture(); await fixture.Apply(fixture.Context());
        var context = fixture.Context(); context.PublishedRuleProblem = "Published mapping is missing";
        Assert.ThrowsAsync<NopException>(() => fixture.Apply(context));
        Assert.That(fixture.Combinations, Has.Count.EqualTo(3));
    }

    [Test]
    public void ConfigurationRejectsDuplicateKeysAndIncompleteMembership()
    {
        var fixture = new Fixture(); fixture.Configuration.Routes[1].Key = "line";
        Assert.Throws<NopException>(() => fixture.Configuration.Validate());
        fixture.Configuration.Routes[1].Key = "city"; fixture.Configuration.Routes[0].MembershipValue = null;
        Assert.Throws<NopException>(() => fixture.Configuration.Validate());
        Assert.Throws<NopException>(() => AkeneoCombinationRouting.Parse("null"));
        Assert.Throws<NopException>(() => AkeneoCombinationRouting.Parse("{}"));
    }
}
