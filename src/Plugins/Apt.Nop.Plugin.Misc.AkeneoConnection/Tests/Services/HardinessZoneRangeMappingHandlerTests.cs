using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services.CustomMappings;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services.Planning;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

[TestFixture]
public class HardinessZoneRangeMappingHandlerTests
{
    private const int SpecId = 5;
    private const int ProductId = 10;

    private Mock<IAkeneoReferenceEntityValueResolver> _records = null!;
    private Mock<ISpecificationAttributeService> _specs = null!;
    private Mock<IAkeneoManagedRelationService> _relations = null!;
    private List<SpecificationAttributeOption> _options = null!;
    private List<ProductSpecificationAttribute> _assignments = null!;
    private List<ProductSpecificationAttribute> _inserted = null!;
    private List<AkeneoManagedRelation> _owned = null!;
    private HardinessZoneRangeMappingHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _records = new Mock<IAkeneoReferenceEntityValueResolver>();
        _specs = new Mock<ISpecificationAttributeService>();
        _relations = new Mock<IAkeneoManagedRelationService>();
        _options = Enumerable.Range(1, 10)
            .Select(zone => new SpecificationAttributeOption
            {
                Id = 100 + zone, SpecificationAttributeId = SpecId, Name = $"Hardiness Zone {zone}"
            })
            .ToList();
        _assignments = new List<ProductSpecificationAttribute>();
        _inserted = new List<ProductSpecificationAttribute>();
        _owned = new List<AkeneoManagedRelation>();
        var nextAssignmentId = 500;

        _specs.Setup(s => s.GetSpecificationAttributeByIdAsync(SpecId))
            .ReturnsAsync(new SpecificationAttribute { Id = SpecId, Name = "Hardiness Zone" });
        _specs.Setup(s => s.GetSpecificationAttributeOptionsBySpecificationAttributeAsync(SpecId))
            .ReturnsAsync(() => _options.ToList());
        _specs.Setup(s => s.GetProductSpecificationAttributesAsync(ProductId, It.IsAny<int>(),
                It.IsAny<bool?>(), It.IsAny<bool?>(), It.IsAny<int?>()))
            .ReturnsAsync(() => _assignments.ToList());
        _specs.Setup(s => s.InsertProductSpecificationAttributeAsync(It.IsAny<ProductSpecificationAttribute>()))
            .Callback<ProductSpecificationAttribute>(assignment =>
            {
                assignment.Id = nextAssignmentId++;
                _inserted.Add(assignment);
            })
            .Returns(Task.CompletedTask);
        _relations.Setup(r => r.GetByProductAsync(7, ProductId, AkeneoManagedRelationType.ProductSpecificationAssignment,
                It.IsAny<string>()))
            .ReturnsAsync(() => _owned.ToList());

        _handler = new HardinessZoneRangeMappingHandler(_records.Object, _specs.Object, _relations.Object);
    }

    [Test]
    public async Task Assigns_every_whole_zone_from_low_to_high()
    {
        Zones("3", "8");
        var context = Context();

        var plan = await PlanAndExecuteAsync(context);

        Assert.Multiple(() =>
        {
            Assert.That(_inserted.Select(a => a.SpecificationAttributeOptionId), Is.EqualTo(new[] { 103, 104, 105, 106, 107, 108 }));
            Assert.That(_inserted, Has.All.Matches<ProductSpecificationAttribute>(a =>
                a.ProductId == ProductId && a.AttributeTypeId == (int)SpecificationAttributeType.Option && a.AllowFiltering));
            Assert.That(plan.Warnings, Is.Empty);
        });
        var ownershipCode = HardinessZoneRangeMappingHandler.OwnershipCode(Mapping());
        Assert.That(ownershipCode, Does.EndWith("#" + HardinessZoneRangeMappingHandler.HandlerKey));
        _relations.Verify(r => r.UpsertAsync(7, 20, ProductId, AkeneoManagedRelationType.ProductSpecificationAssignment,
            It.IsAny<int>(), ownershipCode, It.IsAny<string>()), Times.Exactly(6));
        _specs.Verify(s => s.InsertSpecificationAttributeOptionAsync(It.IsAny<SpecificationAttributeOption>()), Times.Never);
        _specs.Verify(s => s.InsertSpecificationAttributeAsync(It.IsAny<SpecificationAttribute>()), Times.Never);
    }

    [Test]
    public async Task A_zone_without_an_option_is_skipped_with_a_warning()
    {
        _options.RemoveAll(option => option.Name == "Hardiness Zone 6");
        Zones("3", "8");

        var plan = await PlanAndExecuteAsync(Context());

        Assert.Multiple(() =>
        {
            Assert.That(_inserted.Select(a => a.SpecificationAttributeOptionId), Is.EqualTo(new[] { 103, 104, 105, 107, 108 }));
            Assert.That(plan.Warnings.Single(), Does.Contain("hardiness zone 6"));
            Assert.That(plan.Operations.Any(o => o.Preview?.ChangeType == AkeneoDryRunOperationType.Review), Is.False);
        });
    }

    [Test]
    public async Task Already_assigned_zones_are_left_alone()
    {
        _assignments.Add(new ProductSpecificationAttribute { Id = 1, ProductId = ProductId, SpecificationAttributeOptionId = 104 });
        Zones("4", "5");

        await PlanAndExecuteAsync(Context());

        Assert.That(_inserted.Select(a => a.SpecificationAttributeOptionId), Is.EqualTo(new[] { 105 }));
    }

    [TestCase("8", "3", "greater than")]
    [TestCase("3.5", "8", "not a whole hardiness zone")]
    [TestCase("abc", "8", "not a whole hardiness zone")]
    [TestCase(null, "8", "no hardiness_zone_low value")]
    [TestCase("3", null, "no hardiness_zone_high value")]
    public async Task Bad_or_missing_values_change_nothing_and_warn(string? low, string? high, string expected)
    {
        Zones(low, high);
        _assignments.Add(new ProductSpecificationAttribute { Id = 1, ProductId = ProductId, SpecificationAttributeOptionId = 102 });

        var plan = await PlanAndExecuteAsync(Context(AkeneoCollectionSyncMode.ReplaceAll));

        Assert.Multiple(() =>
        {
            Assert.That(Review(plan)?.Detail, Does.Contain(expected));
            Assert.That(plan.Warnings.Single(), Does.Contain(expected));
            Assert.That(_inserted, Is.Empty);
        });
        _specs.Verify(s => s.DeleteProductSpecificationAttributeAsync(It.IsAny<ProductSpecificationAttribute>()), Times.Never);
    }

    [Test]
    public async Task A_product_without_a_record_changes_nothing()
    {
        var plan = await PlanAndExecuteAsync(Context(hasValue: false));

        Assert.That(Review(plan)?.Detail, Does.Contain("no plant_info record"));
        Assert.That(_inserted, Is.Empty);
    }

    [Test]
    public async Task A_competing_specification_mapping_blocks_the_handler()
    {
        Zones("3", "4");
        var context = Context(extra: new AkeneoResolvedMappedValue
        {
            Mapping = new AkeneoAttributeMapping
            {
                AkeneoAttributeCode = "zones", NopTargetTypeId = (int)NopTargetType.SpecificationAttribute,
                NopTargetEntityId = SpecId
            },
            HasValue = true
        });

        var plan = await PlanAndExecuteAsync(context);

        Assert.That(Review(plan)?.Detail, Does.Contain("also writes"));
        Assert.That(_inserted, Is.Empty);
    }

    [Test]
    public async Task Replace_managed_removes_only_owned_zones_outside_the_range()
    {
        var ownedStale = new ProductSpecificationAttribute { Id = 1, ProductId = ProductId, SpecificationAttributeOptionId = 102 };
        var manualStale = new ProductSpecificationAttribute { Id = 2, ProductId = ProductId, SpecificationAttributeOptionId = 101 };
        _assignments.AddRange([ownedStale, manualStale]);
        var relation = new AkeneoManagedRelation { Id = 9, NopRelationEntityId = ownedStale.Id };
        _owned.Add(relation);
        Zones("3", "4");

        await PlanAndExecuteAsync(Context(AkeneoCollectionSyncMode.ReplaceManaged));

        _specs.Verify(s => s.DeleteProductSpecificationAttributeAsync(ownedStale), Times.Once);
        _specs.Verify(s => s.DeleteProductSpecificationAttributeAsync(manualStale), Times.Never);
        _relations.Verify(r => r.DeleteAsync(relation), Times.Once);
    }

    [Test]
    public async Task Replace_all_removes_every_zone_outside_the_range()
    {
        var outside = new ProductSpecificationAttribute { Id = 2, ProductId = ProductId, SpecificationAttributeOptionId = 101 };
        var inside = new ProductSpecificationAttribute { Id = 3, ProductId = ProductId, SpecificationAttributeOptionId = 103 };
        _assignments.AddRange([outside, inside]);
        Zones("3", "4");

        await PlanAndExecuteAsync(Context(AkeneoCollectionSyncMode.ReplaceAll));

        _specs.Verify(s => s.DeleteProductSpecificationAttributeAsync(outside), Times.Once);
        _specs.Verify(s => s.DeleteProductSpecificationAttributeAsync(inside), Times.Never);
        _relations.Verify(r => r.DeleteByRelationEntityAsync(
            AkeneoManagedRelationType.ProductSpecificationAssignment, outside.Id), Times.Once);
    }

    [Test]
    public async Task Merge_never_removes_zones()
    {
        _assignments.Add(new ProductSpecificationAttribute { Id = 2, ProductId = ProductId, SpecificationAttributeOptionId = 101 });
        Zones("3", "4");

        await PlanAndExecuteAsync(Context(AkeneoCollectionSyncMode.Merge));

        _specs.Verify(s => s.DeleteProductSpecificationAttributeAsync(It.IsAny<ProductSpecificationAttribute>()), Times.Never);
    }

    [TestCase("Hardiness Zone 3", 3)]
    [TestCase("Zone 10", 10)]
    [TestCase("7", 7)]
    [TestCase("Hardiness Zone 5a", null)]
    [TestCase("Zones 3 to 4", null)]
    [TestCase("Hardiness Zone", null)]
    public void Options_are_matched_by_their_one_whole_number(string name, int? expected)
    {
        Assert.That(HardinessZoneRangeMappingHandler.ZoneOf(new SpecificationAttributeOption { Name = name }), Is.EqualTo(expected));
    }

    [Test]
    public void Validation_requires_the_record_code_field_and_a_specification_attribute()
    {
        var attribute = new AkeneoAttributeDefinition { Code = "plant_info", Type = "akeneo_reference_entity" };

        Assert.Multiple(() =>
        {
            Assert.That(_handler.Validate(Mapping(), attribute), Is.Empty);
            Assert.That(_handler.Validate(Mapping(field: "hardiness_zone_low"), attribute).Single(), Does.Contain("Record code"));
            Assert.That(_handler.Validate(Mapping(specId: null), attribute).Single(), Does.Contain("specification attribute"));
        });
    }

    private void Zones(string? low, string? high)
    {
        Setup(HardinessZoneRangeMappingHandler.LowFieldCode, low);
        Setup(HardinessZoneRangeMappingHandler.HighFieldCode, high);

        void Setup(string field, string? value) =>
            _records.Setup(r => r.ResolveAsync(It.IsAny<AkeneoProductDefinition>(),
                    It.Is<AkeneoAttributeMapping>(m => m.AkeneoReferenceEntityAttributeCode == field && m.AkeneoAttributeCode == "plant_info"),
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(value == null ? null : new AkeneoResolvedProductValue
                {
                    AttributeCode = "plant_info", DisplayValue = value, DisplayValues = [value]
                });
    }

    private async Task<AkeneoExecutableSectionPlan> PlanAndExecuteAsync(AkeneoProductSyncContext context)
    {
        var plan = new AkeneoExecutableSectionPlan();
        await _handler.PlanAsync(new AkeneoCustomMappingContext
        {
            ProductContext = context,
            MappedValue = context.MappedValues.First()
        }, plan);
        await plan.ExecuteAsync(context);
        return plan;
    }

    private static AkeneoDryRunOperationPreviewModel? Review(AkeneoExecutableSectionPlan plan) =>
        plan.Operations.Select(o => o.Preview).SingleOrDefault(p => p?.ChangeType == AkeneoDryRunOperationType.Review);

    private static AkeneoAttributeMapping Mapping(string field = "@code", int? specId = SpecId) => new()
    {
        AkeneoAttributeCode = "plant_info",
        AkeneoReferenceEntityCode = "plant_info",
        AkeneoReferenceEntityAttributeCode = field,
        NopTargetTypeId = (int)NopTargetType.CustomHandler,
        NopTargetKey = HardinessZoneRangeMappingHandler.HandlerKey,
        NopTargetEntityId = specId
    };

    private static AkeneoProductSyncContext Context(
        AkeneoCollectionSyncMode mode = AkeneoCollectionSyncMode.ReplaceManaged,
        bool hasValue = true,
        AkeneoResolvedMappedValue? extra = null)
    {
        var product = new Product { Id = ProductId };
        var mapped = new AkeneoResolvedMappedValue
        {
            Mapping = Mapping(),
            HasValue = hasValue,
            Value = hasValue
                ? new AkeneoResolvedProductValue
                {
                    AttributeCode = "plant_info", DisplayValue = "red_maple",
                    RawData = JsonSerializer.SerializeToElement("red_maple")
                }
                : null
        };

        return new AkeneoProductSyncContext
        {
            Source = new AkeneoProductDefinition
            {
                Code = "red_maple", Family = "nursery",
                Values = JsonSerializer.SerializeToElement(new Dictionary<string, object>())
            },
            Request = new AkeneoProductImportRequest
            {
                SyncProfileId = 7, SyncRunRecordId = 20, SpecificationAttributeSyncMode = mode,
                Locale = "en_US", Channel = "ecommerce", Currency = "USD"
            },
            Result = new AkeneoProductImportResult(),
            ExistingProduct = product,
            Product = product,
            MappedValues = extra == null ? [mapped] : [mapped, extra]
        };
    }
}
