using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

/// <summary>
/// Select-attribute values are matched to, and create, nopCommerce specification options by
/// their Akeneo option label (case-insensitively), never by the option code.
/// </summary>
[TestFixture]
public class AkeneoSpecificationOptionLabelTests
{
    private Mock<ISpecificationAttributeService> _specifications = null!;
    private Mock<IAkeneoNopEntityMappingService> _entityMappings = null!;
    private Mock<IAkeneoApiClient> _api = null!;
    private readonly List<SpecificationAttributeOption> _createdOptions = [];
    private readonly List<ProductSpecificationAttribute> _assignments = [];

    [SetUp]
    public void SetUp()
    {
        _createdOptions.Clear();
        _assignments.Clear();
        _specifications = new Mock<ISpecificationAttributeService>();
        _specifications.SetReturnsDefault(Task.FromResult<IPagedList<SpecificationAttribute>>(
            new PagedList<SpecificationAttribute>([new SpecificationAttribute { Id = 5, Name = "Species" }], 0, int.MaxValue)));
        _specifications.SetReturnsDefault(Task.FromResult<IList<ProductSpecificationAttribute>>([]));
        _specifications
            .Setup(service => service.InsertSpecificationAttributeOptionAsync(It.IsAny<SpecificationAttributeOption>()))
            .Callback<SpecificationAttributeOption>(option => { option.Id = 51; _createdOptions.Add(option); })
            .Returns(Task.CompletedTask);
        _specifications
            .Setup(service => service.InsertProductSpecificationAttributeAsync(It.IsAny<ProductSpecificationAttribute>()))
            .Callback<ProductSpecificationAttribute>(assignment => { assignment.Id = 60; _assignments.Add(assignment); })
            .Returns(Task.CompletedTask);

        _entityMappings = new Mock<IAkeneoNopEntityMappingService>();
        _entityMappings.SetReturnsDefault(Task.FromResult<int?>(null));
        _entityMappings.SetReturnsDefault(Task.FromResult(true));

        // "red_maple" is labelled; "unlabelled" has no label in any locale.
        _api = new Mock<IAkeneoApiClient>();
        _api.Setup(client => client.GetAttributeOptionDefinitionsAsync("species", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AkeneoAttributeOptionDefinition>
            {
                new() { Code = "red_maple", Attribute = "species", Labels = new() { ["en_US"] = "Red Maple" } },
                new() { Code = "unlabelled", Attribute = "species", Labels = new() }
            });
    }

    [Test]
    public async Task ExistingOptionIsMatchedByLabelCaseInsensitivelyEvenWhenThePayloadOnlyHasTheCode()
    {
        GivenOptions(new SpecificationAttributeOption { Id = 50, SpecificationAttributeId = 5, Name = "red maple" });

        // No linked_data label in the payload: the display value is the bare code.
        await SynchronizeAsync("red_maple", payloadLabel: "red_maple");

        Assert.That(_createdOptions, Is.Empty);
        Assert.That(_assignments.Single().SpecificationAttributeOptionId, Is.EqualTo(50));
        // The case-only mismatch is corrected to the exact Akeneo label.
        _specifications.Verify(service => service.UpdateSpecificationAttributeOptionAsync(
            It.Is<SpecificationAttributeOption>(option => option.Id == 50 && option.Name == "Red Maple")));
    }

    [Test]
    public async Task MissingOptionIsCreatedWithTheLabelNotTheCode()
    {
        GivenOptions(new SpecificationAttributeOption { Id = 40, SpecificationAttributeId = 5, Name = "Sugar Maple" });

        await SynchronizeAsync("red_maple", payloadLabel: "red_maple");

        Assert.That(_createdOptions.Single().Name, Is.EqualTo("Red Maple"));
        Assert.That(_assignments.Single().SpecificationAttributeOptionId, Is.EqualTo(51));
    }

    [Test]
    public async Task AnOptionWithNoLabelIsNeitherMatchedNorCreatedFromItsCode()
    {
        // A nopCommerce option named like the code must not be matched either.
        GivenOptions(new SpecificationAttributeOption { Id = 50, SpecificationAttributeId = 5, Name = "unlabelled" });

        await SynchronizeAsync("unlabelled", payloadLabel: "unlabelled");

        Assert.That(_createdOptions, Is.Empty);
        Assert.That(_assignments, Is.Empty);
    }

    private void GivenOptions(params SpecificationAttributeOption[] options) =>
        _specifications.SetReturnsDefault(Task.FromResult<IList<SpecificationAttributeOption>>(options.ToList()));

    private async Task SynchronizeAsync(string optionCode, string payloadLabel)
    {
        var mapped = new AkeneoResolvedMappedValue
        {
            Mapping = new AkeneoAttributeMapping
            {
                AkeneoAttributeCode = "species",
                AkeneoAttributeTypeId = (int)AkeneoAttributeType.Select,
                NopTargetTypeId = (int)NopTargetType.SpecificationAttribute,
                NopTargetEntityId = 5
            },
            HasValue = true,
            Value = new AkeneoResolvedProductValue
            {
                AttributeCode = "species",
                SourceAttributeType = "pim_catalog_simpleselect",
                DisplayValue = payloadLabel,
                DisplayValues = [payloadLabel],
                RawData = JsonSerializer.SerializeToElement(optionCode)
            },
            ResolvedSourceDisplayName = "species"
        };
        var product = new Product { Id = 10, Name = "Red Maple" };
        var context = new AkeneoProductSyncContext
        {
            Source = new AkeneoProductDefinition
            {
                Uuid = "uuid-1",
                Identifier = "1038",
                Family = "nursery",
                Enabled = true,
                Values = JsonSerializer.SerializeToElement(new Dictionary<string, object>())
            },
            SourceEntityType = AkeneoEntityType.Product,
            Request = new AkeneoProductImportRequest
            {
                Locale = "en_US",
                SpecificationAttributeSyncMode = AkeneoCollectionSyncMode.Merge
            },
            Result = new AkeneoProductImportResult(),
            MappedValues = [mapped],
            SourceCode = "1038",
            SourceUuid = "uuid-1",
            MappingFamilyCode = "nursery",
            MappingEntityScope = AkeneoAttributeMappingEntityScope.StandaloneProduct,
            ProductKey = "1038",
            Sku = "1038",
            ExistingProduct = product,
            Product = product
        };

        var synchronizer = new AkeneoProductSpecificationSynchronizer(
            _specifications.Object,
            _entityMappings.Object,
            new Mock<IAkeneoManagedRelationService>().Object,
            _api.Object);
        await synchronizer.SynchronizeAsync(context);
    }
}
