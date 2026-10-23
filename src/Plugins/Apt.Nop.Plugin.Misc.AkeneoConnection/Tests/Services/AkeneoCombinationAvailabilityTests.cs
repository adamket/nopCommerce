using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Moq;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;
using NUnit.Framework;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

[TestFixture]
public class AkeneoCombinationAvailabilityTests
{
    private static AkeneoVariantImportContext Context(bool? enabled, int? combinationId = 200) => new()
    {
        AkeneoIdentifier = "SKU", AkeneoUuid = "uuid", AkeneoFamilyCode = "family", Sku = "SKU",
        SourceProduct = new AkeneoProductDefinition
        {
            Identifier = "SKU", Uuid = "uuid", Parent = "model", Family = "family", Enabled = enabled,
            Values = JsonSerializer.SerializeToElement(new { })
        },
        SyncProfileId = 7, SyncRunRecordId = 23, ExistingProductAttributeCombinationId = combinationId,
        CombinationAvailabilityCondition = "enabled == true"
    };

    private static AkeneoProductSyncState State() => new()
    {
        Id = 42, SyncProfileId = 7, NopProductId = 100, NopParentProductId = 100,
        NopProductAttributeCombinationId = 200,
        DestinationKindId = (int)AkeneoProductDestinationKind.ProductAttributeCombination,
        LifecycleStatusId = (int)AkeneoProductLifecycleStatus.Active
    };

    private sealed class Fixture
    {
        public readonly Mock<IProductService> Products = new(MockBehavior.Strict);
        public readonly Mock<IProductAttributeService> Attributes = new();
        public readonly Mock<IAkeneoManagedRelationService> Relations = new();
        public readonly Mock<IAkeneoProductSyncStateService> States = new();
        public readonly Product Parent = new() { Id = 100, ProductType = ProductType.SimpleProduct, ManageInventoryMethod = ManageInventoryMethod.ManageStockByAttributes, Published = true, Name = "Parent" };
        public readonly List<ProductAttributeCombination> Combinations = new();
        public readonly List<ProductAttributeValue> Values = new();
        public readonly List<AkeneoFamilyMapping> Families = new() { new() { AkeneoFamilyCode = "family", Enabled = true,
            VariantRelationshipMode = AkeneoVariantRelationshipMode.ProductAttributeCombinations, CombinationAvailabilityCondition = "enabled == true" } };
        public readonly Mock<IAkeneoApiClient> Api = new();
        public readonly AkeneoCombinationAvailabilityService Service;
        public Fixture()
        {
            Products.Setup(x => x.GetProductByIdAsync(100)).ReturnsAsync(Parent);
            Attributes.Setup(x => x.GetAllProductAttributeCombinationsAsync(100)).ReturnsAsync(() => Combinations.ToList());
            Attributes.Setup(x => x.GetProductAttributeCombinationByIdAsync(It.IsAny<int>()))
                .ReturnsAsync((int id) => Combinations.FirstOrDefault(c => c.Id == id)!);
            Attributes.Setup(x => x.DeleteProductAttributeCombinationAsync(It.IsAny<ProductAttributeCombination>()))
                .Callback<ProductAttributeCombination>(c => Combinations.Remove(c)).Returns(Task.CompletedTask);
            Attributes.Setup(x => x.GetProductAttributeValueByIdAsync(It.IsAny<int>()))
                .ReturnsAsync((int id) => Values.FirstOrDefault(v => v.Id == id)!);
            Attributes.Setup(x => x.DeleteProductAttributeValueAsync(It.IsAny<ProductAttributeValue>()))
                .Callback<ProductAttributeValue>(v => Values.Remove(v)).Returns(Task.CompletedTask);
            Relations.Setup(x => x.GetByRelationEntityAsync(7, It.IsAny<AkeneoManagedRelationType>(), It.IsAny<int>()))
                .ReturnsAsync((int profile, AkeneoManagedRelationType kind, int id) => new AkeneoManagedRelation
                { SyncProfileId = profile, NopProductId = 100, RelationTypeId = (int)kind, NopRelationEntityId = id });
            States.Setup(x => x.GetBySourceAsync(7, AkeneoEntityType.Product, "SKU", "uuid")).ReturnsAsync(State());
            var families = new Mock<IAkeneoFamilyMappingService>();
            families.Setup(x => x.GetAllAsync()).ReturnsAsync(Families);
            Api.Setup(x => x.GetProductModelByCodeAsync("model", It.IsAny<CancellationToken>())).ReturnsAsync(new AkeneoProductDefinition { Code = "model", FamilyVariant = "sizes" });
            Service = new(new AkeneoValueTemplateRenderer(new AkeneoProductValueResolver()), States.Object, Products.Object,
                Attributes.Object, Relations.Object, new AkeneoManagedVariantCleanupService(Attributes.Object, Relations.Object), families.Object, Api.Object);
        }
    }

    [TestCase(true, true)]
    [TestCase(false, false)]
    public async Task Enabled_condition_returns_expected_availability(bool enabled, bool expected)
    {
        var fixture = new Fixture();
        Assert.That(await fixture.Service.IsAvailableAsync(Context(enabled)), Is.EqualTo(expected));
    }

    [Test]
    public async Task Availability_uses_source_identifier_before_and_after_destination_mapping()
    {
        var fixture = new Fixture();
        var context = Context(true);
        context.Sku = "mapped-destination-sku";
        context.CombinationAvailabilityCondition = "sku == SKU";
        Assert.That(await fixture.Service.IsAvailableAsync(context), Is.True);
    }

    [Test]
    public void Missing_enabled_value_blocks_instead_of_deleting()
    {
        var fixture = new Fixture();
        Assert.ThrowsAsync<NopException>(() => fixture.Service.IsAvailableAsync(Context(null)));
        fixture.Attributes.VerifyNoOtherCalls();
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task Blank_rule_preserves_previous_behavior(string? condition)
    {
        var fixture = new Fixture(); var context = Context(false); context.CombinationAvailabilityCondition = condition!;
        Assert.That(await fixture.Service.IsAvailableAsync(context), Is.True);
    }

    [TestCase(AkeneoProductEnabledFilter.EnabledOnly)]
    [TestCase(AkeneoProductEnabledFilter.DisabledOnly)]
    public void Availability_rejects_filters_that_hide_one_side_of_the_transition(AkeneoProductEnabledFilter filter)
    {
        Assert.Throws<ArgumentException>(() => AkeneoCombinationAvailabilityService.ValidateConfiguration("enabled == true", filter));
    }

    [Test]
    public void Invalid_rule_is_rejected_before_saving_or_running()
    {
        Assert.Throws<ArgumentException>(() => AkeneoCombinationAvailabilityService.ValidateConfiguration("enabled = true", AkeneoProductEnabledFilter.Any));
    }

    [Test]
    public void Availability_cannot_bypass_create_only_write_policy()
    {
        Assert.Throws<ArgumentException>(() => AkeneoCombinationAvailabilityService.ValidateConfiguration("enabled == true", AkeneoProductEnabledFilter.Any, false));
    }

    [Test]
    public void Rule_changes_invalidate_the_profile_scope()
    {
        var profile = new AkeneoSyncProfile { Id = 7 };
        var before = AkeneoSyncScopeHasher.Build(profile);
        var fingerprint = "changed-family-rule";
        Assert.That(AkeneoSyncScopeHasher.Build(profile, familyAvailabilityFingerprint: fingerprint), Is.Not.EqualTo(before));
    }

    [Test]
    public async Task Disable_removes_only_selected_combination_and_unused_owned_axis_values()
    {
        var fixture = new Fixture();
        var removed = new ProductAttributeCombination { Id = 200, ProductId = 100, Sku = "SKU", AttributesXml = Xml(10, 11, 12) };
        var remaining = new ProductAttributeCombination { Id = 201, ProductId = 100, Sku = "OTHER", AttributesXml = Xml(10) };
        fixture.Combinations.AddRange(new[] { removed, remaining });
        fixture.Values.AddRange(new[] { new ProductAttributeValue { Id = 10 }, new ProductAttributeValue { Id = 11 }, new ProductAttributeValue { Id = 12 } });
        fixture.Relations.Setup(x => x.GetByRelationEntityAsync(7, AkeneoManagedRelationType.ProductAttributeValue, 12))
            .ReturnsAsync((AkeneoManagedRelation)null!);
        Assert.That(await fixture.Service.RemoveAsync(fixture.Parent, Context(false)), Is.True);
        Assert.That(fixture.Combinations.Select(c => c.Id), Is.EqualTo(new[] { 201 }));
        Assert.That(fixture.Values.Select(v => v.Id), Is.EquivalentTo(new[] { 10, 12 }));
        Assert.That(fixture.Parent.Published, Is.True);
        fixture.Products.VerifyNoOtherCalls();
    }

    [Test]
    public async Task Unbound_disabled_sku_creates_no_combination_or_axis_values()
    {
        var fixture = new Fixture();
        var service = RelationshipService(fixture);
        var result = await service.ApplyAsync(fixture.Parent, Context(false, null), () => throw new AssertionException("Child write"));
        Assert.That(result.CombinationUnavailable, Is.True);
        Assert.That(result.Changed, Is.False);
        fixture.Attributes.VerifyNoOtherCalls();
        fixture.Products.VerifyNoOtherCalls();
    }

    [Test]
    public async Task Reenabled_sku_recreates_combination_with_the_same_sku_and_parent()
    {
        var fixture = new Fixture();
        fixture.Combinations.Add(new ProductAttributeCombination { Id = 200, ProductId = 100, Sku = "SKU", AttributesXml = Xml(10) });
        var service = RelationshipService(fixture);
        var disabled = await service.ApplyAsync(fixture.Parent, Context(false), () => throw new AssertionException("Child write"));
        Assert.That(disabled.CombinationUnavailable, Is.True);
        Assert.That(fixture.Combinations, Is.Empty);
        var value = new ProductAttributeValue { Id = 15, ProductAttributeMappingId = 300, Name = "Small", AttributeValueTypeId = (int)AttributeValueType.Simple };
        fixture.Values.Add(value);
        fixture.Attributes.Setup(x => x.GetProductAttributeMappingsByProductIdAsync(100)).ReturnsAsync(new List<ProductAttributeMapping>
        {
            new() { Id = 300, ProductId = 100, ProductAttributeId = 400, AttributeControlTypeId = (int)AttributeControlType.DropdownList, IsRequired = true }
        });
        fixture.Attributes.Setup(x => x.GetProductAttributeValuesAsync(300)).ReturnsAsync(() => fixture.Values.ToList());
        fixture.Attributes.Setup(x => x.InsertProductAttributeCombinationAsync(It.IsAny<ProductAttributeCombination>()))
            .Callback<ProductAttributeCombination>(c => { c.Id = 202; fixture.Combinations.Add(c); }).Returns(Task.CompletedTask);
        var context = Context(true); context.StockQuantity = 5; context.Price = 12;
        context.AxisValuesByAkeneoCode["size"] = new AkeneoVariantAxisValue { AkeneoAttributeCode = "size", AkeneoOptionCode = "small", DisplayName = "Small" };
        var restored = await service.ApplyAsync(fixture.Parent, context, () => throw new AssertionException("Child write"));
        Assert.That(restored.CombinationUnavailable, Is.False);
        Assert.That(restored.DestinationCreated, Is.True);
        Assert.That(restored.NopProductAttributeCombinationId, Is.EqualTo(202));
        Assert.That(fixture.Combinations.Single().Sku, Is.EqualTo("SKU"));
        Assert.That(fixture.Combinations.Single().ProductId, Is.EqualTo(100));
        Assert.That(fixture.Combinations.Single().StockQuantity, Is.EqualTo(5));
        Assert.That(fixture.Parent.Published, Is.True);
        fixture.Products.VerifyNoOtherCalls();
    }

    [Test]
    public void Unmanaged_combination_is_not_removed()
    {
        var fixture = new Fixture();
        fixture.Combinations.Add(new ProductAttributeCombination { Id = 200, ProductId = 100 });
        fixture.States.Setup(x => x.GetBySourceAsync(7, AkeneoEntityType.Product, "SKU", "uuid")).ReturnsAsync((AkeneoProductSyncState)null!);
        fixture.Relations.Setup(x => x.GetByRelationEntityAsync(7, AkeneoManagedRelationType.ProductAttributeCombination, 200)).ReturnsAsync((AkeneoManagedRelation)null!);
        Assert.ThrowsAsync<NopException>(() => fixture.Service.RemoveAsync(fixture.Parent, Context(false)));
        Assert.That(fixture.Combinations.Count, Is.EqualTo(1));
    }

    [Test]
    public void Combination_on_another_parent_is_not_removed()
    {
        var fixture = new Fixture(); fixture.Combinations.Add(new ProductAttributeCombination { Id = 200, ProductId = 999 });
        Assert.ThrowsAsync<NopException>(() => fixture.Service.RemoveAsync(fixture.Parent, Context(false)));
        Assert.That(fixture.Combinations.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task Deleted_parent_is_preserved()
    {
        var fixture = new Fixture(); fixture.Parent.Deleted = true;
        Assert.That(await fixture.Service.RemoveAsync(fixture.Parent, Context(false)), Is.False);
        fixture.Attributes.VerifyNoOtherCalls();
    }

    [Test]
    public async Task Availability_preview_is_read_only()
    {
        var fixture = new Fixture(); fixture.Combinations.Add(new ProductAttributeCombination { Id = 200, ProductId = 100 });
        var plan = await fixture.Service.GetUnavailableBindingAsync(Context(false).SourceProduct,
            new AkeneoProductImportRequest { SyncProfileId = 7 });
        Assert.That(plan, Is.Not.Null);
        Assert.That(plan.CombinationExists, Is.True);
        Assert.That(fixture.Combinations.Count, Is.EqualTo(1));
        fixture.Attributes.Verify(x => x.DeleteProductAttributeCombinationAsync(It.IsAny<ProductAttributeCombination>()), Times.Never);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Batch_removal_precedes_parent_writes_and_marks_the_unavailable_binding_seen(bool held)
    {
        var fixture = new Fixture();
        fixture.Combinations.Add(new ProductAttributeCombination { Id = 200, ProductId = 100, AttributesXml = Xml(10) });
        var source = Context(false).SourceProduct;
        var api = new Mock<IAkeneoApiClient>();
        api.Setup(x => x.GetProductByUuidAsync("uuid", It.IsAny<CancellationToken>())).ReturnsAsync(source);
        api.Setup(x => x.GetProductModelByCodeAsync("model", It.IsAny<CancellationToken>())).ReturnsAsync(new AkeneoProductDefinition
        { Code = "model", Family = "family", Values = JsonSerializer.SerializeToElement(new { }) });
        var skip = new Mock<IAkeneoProfileRuleEvaluator>();
        skip.Setup(x => x.GetSkipReasonAsync(source, It.IsAny<AkeneoProductImportRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(held ? "Held by profile" : null!);
        AkeneoProductImportResult? marked = null;
        fixture.States.Setup(x => x.MarkSeenAsync(7, 23, AkeneoEntityType.Product, "SKU", "uuid", "model", It.IsAny<AkeneoProductImportResult>(), null))
            .Callback<int, int, AkeneoEntityType, string, string, string, AkeneoProductImportResult, string>((_, _, _, _, _, _, result, _) => marked = result)
            .ReturnsAsync(State());
        var batch = new AkeneoProductBatchSyncService(api.Object, Mock.Of<IAkeneoProductValueResolver>(MockBehavior.Strict),
            Mock.Of<IAkeneoNopEntityMappingService>(MockBehavior.Strict), Mock.Of<IAkeneoSyncItemLogService>(), fixture.Products.Object,
            Mock.Of<IAkeneoFamilyMappingService>(MockBehavior.Strict), new AkeneoProductModelHierarchyResolver(),
            Mock.Of<IAkeneoVariantRelationshipService>(MockBehavior.Strict), Mock.Of<IAkeneoLeafRepresentationClassifier>(MockBehavior.Strict),
            Mock.Of<IAkeneoProductSyncService>(MockBehavior.Strict), fixture.States.Object,
            Mock.Of<IAkeneoVariantRepresentationCleanupService>(MockBehavior.Strict), Mock.Of<IAkeneoCatalogReconciliationService>(MockBehavior.Strict),
            Mock.Of<IAkeneoSyncLeaseService>(), Mock.Of<IAkeneoProductModelDeltaFanOutService>(), Mock.Of<IAkeneoDryRunChangeAnalyzer>(MockBehavior.Strict),
            Mock.Of<IAkeneoAssetMappingService>(MockBehavior.Strict), Mock.Of<IAkeneoAssetResolver>(MockBehavior.Strict), skip.Object,
            combinationAvailabilityService: fixture.Service);
        var result = await batch.SyncProductByUuidAsync(new AkeneoProductImportRequest
        { AkeneoProductUuid = "uuid", SyncProfileId = 7, SyncRunRecordId = 23 });
        Assert.That(result.Errors, Is.Empty);
        Assert.That(result.CombinationUnavailable, Is.EqualTo(!held));
        Assert.That(fixture.Combinations.Count, Is.EqualTo(held ? 1 : 0));
        Assert.That(result.NopProductId, Is.EqualTo(100));
        if (!held)
        {
            Assert.That(marked, Is.Not.Null);
            Assert.That(marked!.NopProductAttributeCombinationId, Is.Null);
            Assert.That(marked.CombinationUnavailable, Is.True);
        }
        else Assert.That(marked, Is.Null);
        fixture.Products.Verify(x => x.UpdateProductAsync(It.IsAny<Product>()), Times.Never);
    }

    private static AkeneoVariantRelationshipService RelationshipService(Fixture fixture)
    {
        var resolver = new Mock<IAkeneoVariantRelationshipResolver>();
        resolver.Setup(x => x.ResolveAsync(fixture.Parent, "family", null)).ReturnsAsync(new AkeneoVariantRelationshipResolution
        {
            Mode = AkeneoVariantRelationshipMode.ProductAttributeCombinations,
            Options = new AkeneoVariantRelationshipOptions
            {
                Mode = AkeneoVariantRelationshipMode.ProductAttributeCombinations,
                AxisMappings = new List<AkeneoFamilyVariantAxisMapping>
                {
                    new() { AkeneoAttributeCode = "size", NopProductAttributeId = 400, IsRequired = true }
                }
            }
        });
        return new(resolver.Object, fixture.Products.Object, fixture.Attributes.Object,
            Mock.Of<IAkeneoNopEntityMappingService>(), fixture.Relations.Object,
            new AkeneoValueTemplateRenderer(new AkeneoProductValueResolver()), fixture.Service);
    }

    [Test]
    public async Task Empty_combination_parent_keeps_its_representation_for_reactivation()
    {
        var fixture = new Fixture();
        fixture.Products.Setup(x => x.GetAssociatedProductsAsync(100, 0, 0, true)).ReturnsAsync(new List<Product>());
        fixture.Attributes.Setup(x => x.GetProductAttributeMappingsByProductIdAsync(100))
            .ReturnsAsync(new List<ProductAttributeMapping> { new() { Id = 10, ProductId = 100 } });
        fixture.Attributes.Setup(x => x.GetProductAttributeValuesAsync(10)).ReturnsAsync(new List<ProductAttributeValue>());
        var detected = await new NopVariantStructureDetector(fixture.Products.Object, fixture.Attributes.Object).DetectAsync(fixture.Parent);
        Assert.That(detected.HasVariantStructure, Is.True);
        Assert.That(detected.IsAmbiguous, Is.False);
        Assert.That(detected.Mode, Is.EqualTo(AkeneoVariantRelationshipMode.ProductAttributeCombinations));
    }

    [Test]
    public async Task Family_variant_rule_overrides_default_including_a_blank_rule()
    {
        var fixture = new Fixture();
        Assert.That(await fixture.Service.GetConditionAsync(Context(false).SourceProduct), Is.EqualTo("enabled == true"));
        fixture.Families.Add(new AkeneoFamilyMapping { AkeneoFamilyCode = "family", AkeneoFamilyVariantCode = "sizes", Enabled = true,
            VariantRelationshipMode = AkeneoVariantRelationshipMode.ProductAttributeCombinations });
        Assert.That(await fixture.Service.GetConditionAsync(Context(false).SourceProduct), Is.Null);
        fixture.Families[1].CombinationAvailabilityCondition = "enabled != false";
        Assert.That(await fixture.Service.GetConditionAsync(Context(false).SourceProduct), Is.EqualTo("enabled != false"));
        fixture.Api.Verify(x => x.GetProductModelByCodeAsync("model", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public void Unresolved_family_variant_blocks_instead_of_using_the_family_default()
    {
        var fixture = new Fixture();
        fixture.Api.Setup(x => x.GetProductModelByCodeAsync("model", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AkeneoProductDefinition { Code = "model" });
        Assert.ThrowsAsync<NopException>(() => fixture.Service.GetConditionAsync(Context(false).SourceProduct));
    }

    [Test]
    public async Task Standalone_and_non_combination_families_have_no_availability_rule()
    {
        var fixture = new Fixture(); var source = Context(false).SourceProduct;
        source.Parent = null!;
        Assert.That(await fixture.Service.GetConditionAsync(source), Is.Null);
        source.Parent = "model";
        fixture.Families[0].VariantRelationshipMode = AkeneoVariantRelationshipMode.GroupedProducts;
        Assert.That(await fixture.Service.GetConditionAsync(source), Is.Null);
        fixture.Api.VerifyNoOtherCalls();
    }

    [Test]
    public async Task Family_rules_validate_only_profiles_in_their_family_scope()
    {
        var fixture = new Fixture();
        await fixture.Service.ValidateRulesAsync(new[] { "another_family" }, AkeneoProductEnabledFilter.EnabledOnly, false);
        Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.ValidateRulesAsync(new[] { "family" }, AkeneoProductEnabledFilter.EnabledOnly, true));
        await fixture.Service.ValidateRulesAsync(new[] { "family" }, AkeneoProductEnabledFilter.Any, true);
    }

    [Test]
    public async Task Family_rule_changes_invalidate_profile_scope()
    {
        var fixture = new Fixture();
        var before = await fixture.Service.GetConfigurationFingerprintAsync(new[] { "family" });
        fixture.Families[0].CombinationAvailabilityCondition = null!;
        Assert.That(await fixture.Service.GetConfigurationFingerprintAsync(new[] { "family" }), Is.Not.EqualTo(before));
    }

    [Test]
    public void Migration_transfers_only_agreed_rules_and_never_enables_a_blank_profile()
    {
        Assert.That(Data.AkeneoConnectionMigration.SelectCondition("family", new[] { ("family", "enabled == true"), ("other", "enabled == false") }), Is.EqualTo("enabled == true"));
        Assert.That(Data.AkeneoConnectionMigration.SelectCondition("family", new[] { ("family", "enabled == true"), ("", "") }), Is.Null);
        Assert.That(Data.AkeneoConnectionMigration.SelectCondition("family", new[] { ("family", "enabled == true"), ("family", "enabled == false") }), Is.Null);
    }

    private static string Xml(params int[] ids) => "<Attributes>" + string.Concat(ids.Select(id =>
        $"<ProductAttribute ID='{id}'><ProductAttributeValue><Value>{id}</Value></ProductAttributeValue></ProductAttribute>")) + "</Attributes>";
}
