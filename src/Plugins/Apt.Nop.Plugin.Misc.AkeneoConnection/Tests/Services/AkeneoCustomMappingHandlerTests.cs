using System.Text.Json;
using System.Transactions;
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
public class AkeneoCustomMappingHandlerTests
{
    private Mock<IProductService> _products = null!;
    private Mock<IAkeneoApiClient> _api = null!;
    private List<TierPrice> _rows = null!;
    private TierPriceTableMappingHandler _handler = null!;
    private AkeneoCustomMappingRegistry _registry = null!;
    private AkeneoCustomMappingSynchronizer _sync = null!;
    private bool _writesInTransaction;
    private static AkeneoAttributeDefinition Attribute() => new()
    {
        Code = "tier_prices", Type = "pim_catalog_table",
        TableConfiguration = [
            new() { Code = "min_qty", DataType = "number" },
            new() { Code = "tier_price", DataType = "number" }]
    };
    [SetUp]
    public void Setup()
    {
        _rows = [new() { Id = 1, ProductId = 42, Quantity = 5, Price = 20 }];
        _products = new();
        _products.Setup(p => p.GetTierPricesByProductAsync(42)).ReturnsAsync(() => _rows.ToList());
        _writesInTransaction = true;
        _products.Setup(p => p.DeleteTierPriceAsync(It.IsAny<TierPrice>()))
            .Callback<TierPrice>(p => { _writesInTransaction &= Transaction.Current != null; _rows.Remove(p); })
            .Returns(Task.CompletedTask);
        _products.Setup(p => p.InsertTierPriceAsync(It.IsAny<TierPrice>()))
            .Callback<TierPrice>(p => { _writesInTransaction &= Transaction.Current != null; _rows.Add(p); })
            .Returns(Task.CompletedTask);
        _api = new();
        _api.Setup(a => a.GetAttributeByCodeAsync("tier_prices", It.IsAny<CancellationToken>())).ReturnsAsync(Attribute);
        _handler = new(_products.Object);
        _registry = new([_handler]);
        _sync = new(_registry, _api.Object);
    }

    private static AkeneoResolvedMappedValue Mapped(string? json, string key = TierPriceTableMappingHandler.HandlerKey) => new()
    {
        Mapping = new() { Id = 7, AkeneoAttributeCode = "tier_prices", AkeneoAttributeTypeId = (int)AkeneoAttributeType.Table,
            NopTargetTypeId = (int)NopTargetType.CustomHandler, NopTargetKey = key },
        HasValue = json != null,
        Value = json == null ? null : new() { AttributeCode = "tier_prices", SourceAttributeType = "pim_catalog_table",
            RawData = JsonSerializer.Deserialize<JsonElement>(json) }
    };
    private static AkeneoProductSyncContext Context(string? json, bool clear = false,
        IReadOnlyList<AkeneoResolvedMappedValue>? mappings = null) => new()
    {
        Product = new() { Id = 42 }, ExistingProduct = new() { Id = 42 },
        Request = new() { ProductFieldMissingValueBehavior = clear
            ? AkeneoMissingValueBehavior.ClearExisting : AkeneoMissingValueBehavior.PreserveExisting },
        Result = new(), MappedValues = mappings ?? [Mapped(json)]
    };
    private void AssertNoWrites()
    {
        _products.Verify(p => p.DeleteTierPriceAsync(It.IsAny<TierPrice>()), Times.Never);
        _products.Verify(p => p.InsertTierPriceAsync(It.IsAny<TierPrice>()), Times.Never);
        _products.Verify(p => p.UpdateTierPriceAsync(It.IsAny<TierPrice>()), Times.Never);
    }

    [Test]
    public async Task Preview_and_apply_share_the_same_full_replacement_without_preview_writes()
    {
        _rows.Add(new() { Id = 2, ProductId = 42, Quantity = 25, Price = 2, CustomerRoleId = 3,
            StoreId = 4, StartDateTimeUtc = DateTime.UtcNow });
        var context = Context("""[{"min_qty":"10","tier_price":"9.50"},{"min_qty":20,"tier_price":8}]""");
        var preview = new AkeneoProductMappingPreviewModel();
        await _sync.PlanAsync(context, preview);
        AssertNoWrites();
        Assert.That(preview.Operations.Single().ChangeType, Is.EqualTo(AkeneoDryRunOperationType.Update));
        Assert.That(preview.Operations.Single().CurrentValue, Does.Contain("\"CustomerRoleId\":3"));
        await _sync.SynchronizeAsync(context);
        Assert.That(_rows.Select(p => (p.Quantity, p.Price)), Is.EqualTo(new[] { (10, 9.50m), (20, 8m) }));
        Assert.That(_rows.All(p => p.ProductId == 42 && p.CustomerRoleId == null && p.StoreId == 0 &&
            p.StartDateTimeUtc == null && p.EndDateTimeUtc == null), Is.True);
        Assert.That(_writesInTransaction, Is.True);
        Assert.That(context.HasChanges, Is.True);
        _products.Verify(p => p.DeleteTierPriceAsync(It.IsAny<TierPrice>()), Times.Exactly(2));
    }

    [Test]
    public async Task Identical_prices_in_different_row_order_do_not_write()
    {
        _rows.Add(new() { Id = 2, ProductId = 42, Quantity = 10, Price = 10 });
        var context = Context("""[{"min_qty":10,"tier_price":"10.0000"},{"min_qty":5,"tier_price":20}]""");
        await _sync.SynchronizeAsync(context);
        AssertNoWrites();
        Assert.That(context.HasChanges, Is.False);
    }

    [Test]
    public async Task Duplicate_destination_rows_are_replaced_even_when_each_matches_a_source_row()
    {
        _rows.Add(new() { Id = 2, ProductId = 42, Quantity = 5, Price = 20 });
        await _sync.SynchronizeAsync(Context("""[{"min_qty":5,"tier_price":20}]"""));
        Assert.That(_rows, Has.Count.EqualTo(1));
    }

    [TestCase("""[{"min_qty":0,"tier_price":1}]""")]
    [TestCase("""[{"min_qty":1.5,"tier_price":1}]""")]
    [TestCase("""[{"min_qty":2147483648,"tier_price":1}]""")]
    [TestCase("""[{"min_qty":1,"tier_price":-1}]""")]
    [TestCase("""[{"min_qty":1,"tier_price":"1,50"}]""")]
    [TestCase("""[{"min_qty":1,"tier_price":0.00001}]""")]
    [TestCase("""[{"min_qty":1,"tier_price":100000000000000}]""")]
    [TestCase("""[{"min_qty":1,"tier_price":null}]""")]
    [TestCase("""[{"min_qty":1}]""")]
    [TestCase("""[{"min_qty":1,"tier_price":3},{"min_qty":1,"tier_price":4}]""")]
    [TestCase("""[{"min_qty":1,"tier_price":3},false]""")]
    [TestCase("""{}""")]
    public async Task Invalid_rows_block_the_entire_replacement(string json)
    {
        var context = Context(json, clear: true);
        var preview = new AkeneoProductMappingPreviewModel();
        await _sync.PlanAsync(context, preview);
        Assert.That(preview.Operations.Single().ChangeType, Is.EqualTo(AkeneoDryRunOperationType.Review));
        await _sync.SynchronizeAsync(context);
        AssertNoWrites();
        Assert.That(_rows.Single().Price, Is.EqualTo(20));
        Assert.That(context.Result.Warnings, Is.Not.Empty);
    }

    [TestCase(null)]
    [TestCase("null")]
    public async Task Missing_source_preserves_even_with_clear_existing(string? json)
    {
        await _sync.SynchronizeAsync(Context(json, clear: true));
        AssertNoWrites();
    }

    [TestCase(false, 1)]
    [TestCase(true, 0)]
    public async Task Explicit_empty_table_obeys_clear_existing(bool clear, int count)
    {
        await _sync.SynchronizeAsync(Context("[]", clear));
        Assert.That(_rows, Has.Count.EqualTo(count));
        if (!clear) AssertNoWrites();
    }

    [Test]
    public async Task Required_empty_table_reports_error_and_does_not_clear()
    {
        var mapping = Mapped("[]");
        mapping.Mapping.IsRequired = true;
        var context = Context(null, true, [mapping]);
        await _sync.SynchronizeAsync(context);
        AssertNoWrites();
        Assert.That(context.Result.Errors, Is.Not.Empty);
    }

    [Test]
    public async Task Unmapped_handler_never_loads_metadata_or_prices()
    {
        await _sync.SynchronizeAsync(Context(null, mappings: []));
        _products.VerifyNoOtherCalls();
        _api.VerifyNoOtherCalls();
    }

    [Test]
    public async Task Unknown_handler_is_reported_without_writes()
    {
        var context = Context(null, mappings: [Mapped("[]", "removed-handler")]);
        await _sync.SynchronizeAsync(context);
        AssertNoWrites();
        Assert.That(context.Result.Warnings.Single(), Does.Contain("unavailable"));
    }

    [Test]
    public async Task Duplicate_handler_mappings_cannot_compete()
    {
        var context = Context(null, true, [Mapped("[]"), Mapped("""[{"min_qty":10,"tier_price":2}]""")]);
        await _sync.SynchronizeAsync(context);
        AssertNoWrites();
        Assert.That(context.Result.Warnings.Single(), Does.Contain("Multiple mappings"));
    }

    [Test]
    public async Task Changed_schema_blocks_sync()
    {
        var attribute = Attribute();
        attribute.TableConfiguration[1].DataType = "text";
        _api.Setup(a => a.GetAttributeByCodeAsync("tier_prices", It.IsAny<CancellationToken>())).ReturnsAsync(attribute);
        await _sync.SynchronizeAsync(Context("[]", true));
        AssertNoWrites();
    }

    [Test]
    public void Registry_filters_types_and_rejects_duplicate_keys()
    {
        var resolver = new AkeneoTargetTypeResolver(_registry);
        Assert.That(resolver.GetCustomHandlerOptions(Attribute()).Single().Code, Is.EqualTo(_handler.Key));
        Assert.That(resolver.GetAllowedTargetTypes(Attribute()), Does.Contain(NopTargetType.CustomHandler));
        Assert.That(resolver.GetCustomHandlerOptions(new() { Type = "pim_catalog_number" }), Is.Empty);
        Assert.Throws<InvalidOperationException>(() => new AkeneoCustomMappingRegistry([_handler, _handler]));
    }


    [Test]
    public async Task Replacement_joins_the_existing_import_transaction()
    {
        using var outer = new TransactionScope(TransactionScopeOption.Required,
            new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted }, TransactionScopeAsyncFlowOption.Enabled);
        await _sync.SynchronizeAsync(Context("""[{"min_qty":2,"tier_price":3}]"""));
        Assert.That(_writesInTransaction, Is.True);
        outer.Complete();
    }

    [Test]
    public void Failed_replacement_aborts_the_transaction()
    {
        TransactionStatus? status = null;
        _products.Setup(p => p.InsertTierPriceAsync(It.IsAny<TierPrice>()))
            .Callback(() => Transaction.Current!.TransactionCompleted += (_, e) =>
                status = e.Transaction!.TransactionInformation.Status)
            .ThrowsAsync(new InvalidOperationException("simulated failure"));
        Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sync.SynchronizeAsync(Context("""[{"min_qty":2,"tier_price":3}]""")));
        Assert.That(status, Is.EqualTo(TransactionStatus.Aborted));
    }

    [Test]
    public async Task Scalar_and_table_mappings_both_refuse_to_write_when_imported_configuration_conflicts()
    {
        var scalar = new AkeneoResolvedMappedValue { Mapping = new()
        {
            NopTargetTypeId = (int)NopTargetType.TierPrice, AkeneoAttributeCode = "scalar_price",
            NopTargetKey = "1", NopTargetEntityId = 3
        }};
        var context = Context(null, true, [Mapped("[]"), scalar]);
        await _sync.SynchronizeAsync(context);
        var legacy = new AkeneoProductTierPriceSynchronizer(_products.Object,
            Mock.Of<global::Nop.Services.Customers.ICustomerService>(), Mock.Of<IAkeneoManagedRelationService>());
        await legacy.SynchronizeAsync(context);
        AssertNoWrites();
        Assert.That(context.Result.Warnings, Has.Count.EqualTo(2));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Admin_save_checks_schema_and_persists_handler_key(bool validSchema)
    {
        var attribute = Attribute();
        if (!validSchema) attribute.TableConfiguration.RemoveAt(1);
        _api.Setup(a => a.GetAttributeByCodeAsync("tier_prices", It.IsAny<CancellationToken>())).ReturnsAsync(attribute);
        var mappings = new Mock<IAkeneoAttributeMappingService>();
        mappings.Setup(m => m.GetEffectiveMappingsAsync(It.IsAny<string>())).ReturnsAsync(new List<AkeneoAttributeMapping>());
        var controller = new Apt.Nop.Plugin.Misc.AkeneoConnection.Controllers.AkeneoMappingController(
            Mock.Of<global::Nop.Core.IWorkContext>(), Mock.Of<Apt.Nop.Plugin.Misc.AkeneoConnection.Factories.IAkeneoAttributeMappingModelFactory>(),
            mappings.Object, _api.Object, new AkeneoTargetTypeResolver(_registry), Mock.Of<IAkeneoValueTemplateRenderer>(),
            Mock.Of<global::Nop.Services.Messages.INotificationService>(), Mock.Of<IAkeneoNopEntityMappingService>(),
            Mock.Of<global::Nop.Core.IStoreContext>(), Mock.Of<global::Nop.Services.Configuration.ISettingService>(),
            Mock.Of<Apt.Nop.Plugin.Misc.AkeneoConnection.Factories.IAkeneoCategoryMappingModelFactory>(), _registry);
        var model = new AkeneoAttributeMappingModel
        {
            AkeneoAttributeCode = "tier_prices", AkeneoAttributeTypeId = (int)AkeneoAttributeType.Table,
            NopTargetTypeId = (int)NopTargetType.CustomHandler, NopTargetKey = _handler.Key,
            EntityScopeId = (int)AkeneoAttributeMappingEntityScope.All
        };
        var response = (Microsoft.AspNetCore.Mvc.JsonResult)await controller.SaveAttributeMappingRow(model);
        var json = JsonSerializer.SerializeToElement(response.Value);
        Assert.That(json.GetProperty("success").GetBoolean(), Is.EqualTo(validSchema), json.ToString());
        mappings.Verify(m => m.InsertAkeneoAttributeMappingAsync(It.Is<AkeneoAttributeMapping>(a =>
            a.NopTargetTypeId == (int)NopTargetType.CustomHandler && a.NopTargetKey == TierPriceTableMappingHandler.HandlerKey &&
            a.AkeneoAttributeCode == "tier_prices")), validSchema ? Times.Once() : Times.Never());
    }

    [Test]
    public async Task Raw_empty_table_survives_source_resolution_with_locale_and_channel()
    {
        var mappings = new Mock<IAkeneoAttributeMappingService>();
        var mapping = Mapped("[]").Mapping;
        mapping.Locale = "en_US"; mapping.Channel = "ecommerce";
        mappings.Setup(m => m.GetEffectiveMappingsAsync(It.IsAny<string>())).ReturnsAsync(new List<AkeneoAttributeMapping> { mapping });
        mappings.Setup(m => m.GetAllFallbackSourcesAsync()).ReturnsAsync(new List<AkeneoAttributeMappingFallbackSource>());
        var assets = new Mock<IAkeneoAssetMappingService>();
        assets.Setup(m => m.GetEffectiveMappingsAsync(It.IsAny<string>())).ReturnsAsync(new List<AkeneoAssetMapping>());
        var service = new AkeneoProductSyncService(new AkeneoProductValueResolver(),
            Mock.Of<IAkeneoReferenceEntityValueResolver>(), Mock.Of<IAkeneoValueTransformationService>(),
            Mock.Of<IAkeneoValueTemplateRenderer>(), mappings.Object, assets.Object,
            Mock.Of<IAkeneoNopEntityMappingService>(), Mock.Of<IAkeneoProductSyncStateService>(),
            _products.Object, Mock.Of<IAkeneoProductSyncPipeline>(),
            new AkeneoProfileRuleEvaluator(Mock.Of<IAkeneoApiClient>()),
            Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.TestData.AkeneoAttributeCatalogTestData.Create("tier_prices"));
        var source = new AkeneoProductDefinition
        {
            Identifier = "SKU", Uuid = "uuid", Family = "family",
            Values = JsonSerializer.Deserialize<JsonElement>("""
                {"tier_prices":[
                    {"locale":"fr_FR","scope":"ecommerce","data":[{"min_qty":1,"tier_price":55}]},
                    {"locale":"en_US","scope":"ecommerce","data":[],"attribute_type":"pim_catalog_table"}]}
                """)
        };
        var intent = await service.ResolveIntentAsync(source, AkeneoEntityType.Product, new AkeneoProductImportRequest());
        Assert.That(intent.Success, Is.True, string.Join(" ", intent.Errors));
        Assert.That(intent.MappedValues.Single().Value.RawData!.Value.GetArrayLength(), Is.EqualTo(0));
    }
}
