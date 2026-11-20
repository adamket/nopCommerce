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
public class AkeneoVariantPreselectionTests
{
    private Mock<IAkeneoApiClient> _api = null!;
    private Mock<IAkeneoFamilyMappingService> _families = null!;
    private Mock<IAkeneoProductSyncStateService> _states = null!;
    private Mock<IProductService> _products = null!;
    private Mock<IProductAttributeService> _attributes = null!;
    private Mock<IProductAttributeParser> _parser = null!;
    private Mock<IAkeneoDeferredProductEvents> _events = null!;
    private AkeneoVariantPreselectionService _service = null!;
    private AkeneoFamilyMapping _family = null!;
    private AkeneoProductBatchImportRequest _request = null!;
    private AkeneoProductBatchImportResult _result = null!;
    private List<ProductAttributeMapping> _mappings = null!;
    private List<ProductAttributeValue> _values = null!;
    private List<ProductAttributeCombination> _combinations = null!;
    private List<AkeneoProductDefinition> _sources = null!;

    [SetUp]
    public void SetUp()
    {
        _api = new();
        _families = new();
        _states = new();
        _products = new();
        _attributes = new();
        _parser = new();
        _events = new();
        _family = new AkeneoFamilyMapping { Id = 7 };
        _request = new AkeneoProductBatchImportRequest
        {
            RunMode = AkeneoRunMode.Resync,
            SyncProfileId = 1,
        };
        _result = new AkeneoProductBatchImportResult { CompletedAllPages = true };
        _mappings =
        [
            new ProductAttributeMapping
            {
                Id = 100,
                ProductId = 10,
                ProductAttributeId = 1,
                IsRequired = true,
            },
        ];
        _values = [];
        _combinations = [];
        _sources = [];
        _families.Setup(service => service.GetByIdAsync(7)).ReturnsAsync(_family);
        _families
            .Setup(service => service.GetAxisMappingsAsync(7))
            .ReturnsAsync(
                new List<AkeneoFamilyVariantAxisMapping>
                {
                    new() { AkeneoAttributeCode = "size", NopProductAttributeId = 1 },
                    new()
                    {
                        AkeneoAttributeCode = "color",
                        NopProductAttributeId = 2,
                        DisplayOrder = 1,
                    },
                }
            );
        _products
            .Setup(service => service.GetProductByIdAsync(It.IsAny<int>()))
            .ReturnsAsync((int id) => new Product { Id = id });
        _attributes
            .Setup(service => service.GetProductAttributeMappingsByProductIdAsync(10))
            .ReturnsAsync(_mappings);
        _attributes
            .Setup(service => service.GetProductAttributeValuesAsync(It.IsAny<int>()))
            .ReturnsAsync(
                (int id) => _values.Where(value => value.ProductAttributeMappingId == id).ToList()
            );
        _attributes
            .Setup(service => service.GetProductAttributeValueByIdAsync(It.IsAny<int>()))
            .ReturnsAsync((int id) => _values.SingleOrDefault(value => value.Id == id)!);
        _attributes
            .Setup(service => service.GetProductAttributeCombinationByIdAsync(It.IsAny<int>()))
            .ReturnsAsync((int id) => _combinations.SingleOrDefault(value => value.Id == id)!);
        _attributes
            .Setup(service => service.GetAllProductAttributeCombinationsAsync(10))
            .ReturnsAsync(_combinations);
        _attributes
            .Setup(service =>
                service.UpdateProductAttributeValueAsync(It.IsAny<ProductAttributeValue>())
            )
            .Callback(() => Assert.That(Transaction.Current, Is.Not.Null))
            .Returns(Task.CompletedTask);
        _api.Setup(api =>
                api.GetProductsPageAsync(
                    100,
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(() => new AkeneoProductPageResult { Items = _sources });
        _api.Setup(api =>
                api.GetProductModelsPageAsync(
                    100,
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(new AkeneoProductPageResult());
        var cache = new Mock<IAkeneoAttributeValueCacheTracker>();
        cache
            .Setup(service => service.ClearTouchedAsync())
            .Callback(() => Assert.That(Transaction.Current, Is.Null))
            .Returns(Task.CompletedTask);
        _events
            .Setup(service => service.PublishAsync())
            .Callback(() => Assert.That(Transaction.Current, Is.Null))
            .Returns(Task.CompletedTask);
        _service = new(
            _api.Object,
            _families.Object,
            _states.Object,
            _products.Object,
            _attributes.Object,
            _parser.Object,
            cache.Object,
            _events.Object,
            Mock.Of<IAkeneoSyncItemLogService>()
        );
        _service.Begin();
    }

    private ProductAttributeValue AddOption(int id, int order)
    {
        var value = new ProductAttributeValue
        {
            Id = id,
            ProductAttributeMappingId = 100,
            AssociatedProductId = id + 1000,
            AttributeValueTypeId = (int)AttributeValueType.AssociatedToProduct,
            DisplayOrder = order,
            Name = $"Option {id}",
        };
        _values.Add(value);
        var source = new AkeneoProductDefinition { Uuid = id.ToString(), Parent = "parent" };
        _sources.Add(source);
        _service.Observe(
            source,
            new AkeneoProductImportResult
            {
                NopProductId = value.AssociatedProductId,
                NopParentProductId = 10,
                NopProductAttributeValueId = id,
                VariantFamilyMappingId = 7,
                VariantParentModelCode = "parent",
                DestinationKind = AkeneoProductDestinationKind.AssociatedProduct,
            }
        );
        return value;
    }

    private Task<int> Run() => _service.ApplyAsync(_request, _result);

    [Test]
    public async Task A_later_lower_order_variant_wins_only_after_the_batch_finishes()
    {
        AddOption(1, 40);
        AddOption(2, 10);
        Assert.That(_values.Any(value => value.IsPreSelected), Is.False);
        Assert.That(await Run(), Is.EqualTo(1));
        Assert.That(_values.Single(value => value.IsPreSelected).Id, Is.EqualTo(2));
        Assert.That(await Run(), Is.Zero, "Retry must preserve the saved selection.");
        _events.Verify(
            service => service.Defer(It.Is<Product>(product => product.Id == 10)),
            Times.Once
        );
    }

    [Test]
    public async Task Disabled_setting_and_existing_selection_are_preserved()
    {
        var original = AddOption(1, 40);
        AddOption(2, 10);
        _family.AutomaticallyPreselectVariant = false;
        Assert.That(await Run(), Is.Zero);
        Assert.That(_values.Any(value => value.IsPreSelected), Is.False);
        _family.AutomaticallyPreselectVariant = true;
        original.IsPreSelected = true;
        Assert.That(await Run(), Is.Zero);
        Assert.That(_values.Single(value => value.IsPreSelected), Is.SameAs(original));
        Assert.That(_api.Invocations, Is.Empty);
    }

    [TestCase("failed")]
    [TestCase("canceled")]
    [TestCase("truncated")]
    [TestCase("incomplete")]
    [TestCase("cursor")]
    [TestCase("single")]
    [TestCase("write-policy")]
    public async Task Incomplete_runs_and_disallowed_updates_do_not_select(string condition)
    {
        AddOption(1, 0);
        switch (condition)
        {
            case "failed":
                _result.FailedCount = 1;
                break;
            case "canceled":
                _result.Canceled = true;
                break;
            case "truncated":
                _result.WasTruncated = true;
                break;
            case "incomplete":
                _result.CompletedAllPages = false;
                break;
            case "cursor":
                _request.SearchAfter = "cursor";
                break;
            case "single":
                _request.RunMode = AkeneoRunMode.SingleProduct;
                break;
            case "write-policy":
                _request.UpdateExistingProducts = false;
                break;
        }
        Assert.That(await Run(), Is.Zero);
        Assert.That(_api.Invocations, Is.Empty);
        Assert.That(_values[0].IsPreSelected, Is.False);
    }

    [Test]
    public async Task An_unsynchronized_sibling_defers_selection_until_retry()
    {
        AddOption(1, 40);
        _sources.Add(new AkeneoProductDefinition { Uuid = "2", Parent = "parent" });
        Assert.That(await Run(), Is.Zero);
        Assert.That(_values[0].IsPreSelected, Is.False);
        _sources.RemoveAt(1);
        AddOption(2, 0);
        Assert.That(await Run(), Is.EqualTo(1));
        Assert.That(_values.Single(value => value.IsPreSelected).Id, Is.EqualTo(2));
    }

    [Test]
    public async Task Delta_includes_siblings_synchronized_in_an_earlier_run()
    {
        AddOption(1, 40);
        var earlier = AddOption(2, 0);
        _service.Begin();
        _sources.Clear();
        _values.RemoveAt(0);
        AddOption(1, 40);
        _sources.Add(new AkeneoProductDefinition { Uuid = "2", Parent = "parent" });
        _request.RunMode = AkeneoRunMode.Delta;
        _states
            .Setup(service => service.GetBySourceAsync(1, AkeneoEntityType.Product, null, "2"))
            .ReturnsAsync(
                new AkeneoProductSyncState
                {
                    NopProductId = earlier.AssociatedProductId,
                    AkeneoParentCode = "parent",
                    NopParentProductId = 10,
                    NopProductAttributeValueId = earlier.Id,
                    LifecycleStatusId = (int)AkeneoProductLifecycleStatus.Active,
                    DestinationKindId = (int)AkeneoProductDestinationKind.AssociatedProduct,
                }
            );
        Assert.That(await Run(), Is.EqualTo(1));
        Assert.That(earlier.IsPreSelected, Is.True);
    }

    [Test]
    public void Inventory_paging_failure_never_assigns_a_default()
    {
        AddOption(1, 0);
        _api.Setup(api =>
                api.GetProductsPageAsync(
                    100,
                    null,
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(new AkeneoProductPageResult { Items = _sources, SearchAfter = "next" });
        _api.Setup(api =>
                api.GetProductsPageAsync(
                    100,
                    "next",
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(new InvalidOperationException("Page failed"));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await Run());
        Assert.That(_values[0].IsPreSelected, Is.False);
    }

    [Test]
    public async Task Combination_selection_uses_one_real_variant_and_preserves_partial_selections()
    {
        _mappings.Add(
            new ProductAttributeMapping
            {
                Id = 200,
                ProductId = 10,
                ProductAttributeId = 2,
                IsRequired = true,
            }
        );
        _values.AddRange(
            new[]
            {
                new ProductAttributeValue
                {
                    Id = 1,
                    ProductAttributeMappingId = 100,
                    DisplayOrder = 0,
                },
                new ProductAttributeValue
                {
                    Id = 2,
                    ProductAttributeMappingId = 100,
                    DisplayOrder = 10,
                },
                new ProductAttributeValue
                {
                    Id = 3,
                    ProductAttributeMappingId = 200,
                    DisplayOrder = 0,
                },
                new ProductAttributeValue
                {
                    Id = 4,
                    ProductAttributeMappingId = 200,
                    DisplayOrder = 10,
                },
            }
        );
        // The independently lowest pair (1, 3) does not exist.
        foreach (var id in new[] { 11, 12 })
        {
            _combinations.Add(
                new ProductAttributeCombination
                {
                    Id = id,
                    ProductId = 10,
                    AttributesXml = id.ToString(),
                }
            );
            var source = new AkeneoProductDefinition { Uuid = id.ToString(), Parent = "parent" };
            _sources.Add(source);
            _service.Observe(
                source,
                new AkeneoProductImportResult
                {
                    NopProductId = 10,
                    NopParentProductId = 10,
                    NopProductAttributeCombinationId = id,
                    VariantFamilyMappingId = 7,
                    VariantParentModelCode = "parent",
                    DestinationKind = AkeneoProductDestinationKind.ProductAttributeCombination,
                }
            );
        }
        _parser
            .Setup(parser => parser.ParseProductAttributeValuesAsync("11", 0))
            .ReturnsAsync(new[] { _values[0], _values[3] });
        _parser
            .Setup(parser => parser.ParseProductAttributeValuesAsync("12", 0))
            .ReturnsAsync(new[] { _values[1], _values[2] });
        Assert.That(await Run(), Is.EqualTo(1));
        Assert.That(
            _values.Where(value => value.IsPreSelected).Select(value => value.Id),
            Is.EquivalentTo(new[] { 1, 4 })
        );

        foreach (var value in _values)
            value.IsPreSelected = false;
        _family.VariantDisplayOrderKeys = "color, size";
        Assert.That(await Run(), Is.EqualTo(1));
        Assert.That(
            _values.Where(value => value.IsPreSelected).Select(value => value.Id),
            Is.EquivalentTo(new[] { 2, 3 })
        );
        _values[2].IsPreSelected = false;
        Assert.That(await Run(), Is.Zero, "A partial existing selection must also be preserved.");
        Assert.That(_values.Single(value => value.IsPreSelected).Id, Is.EqualTo(2));
    }
}
