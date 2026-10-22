using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Moq;
using Nop.Core.Domain.Catalog;
using Nop.Core.Events;
using Nop.Data;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

[TestFixture]
public class AkeneoPublicationTemplateTests
{
    private const string Template = "{sync_status == published ? true : false}";
    private static AkeneoProductDefinition Source(string? status, string? channel = "ecommerce") => new()
    {
        Uuid = "uuid", Identifier = "SKU", Enabled = true,
        Values = JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            ["sync_status"] = new[] { new { data = status, scope = channel, locale = (string?)null } }
        })
    };

    private static AkeneoProductImportRequest Request() => new()
    {
        SyncProfileId = 7, SyncRunRecordId = 23, Locale = "en_US", Channel = "ecommerce", Currency = "USD",
        SkipRulesJson = """[{"attributeCode":"sync_status","value":"hold"}]"""
    };

    [TestCase("published", true)]
    [TestCase("unpublished", false)]
    [TestCase("draft", false)]
    public async Task Conditional_template_writes_Published_instead_of_using_enabled(string status, bool expected)
    {
        var products = new Mock<IProductService>();
        var product = new Product { Id = 100, Sku = "SKU", Published = !expected };
        products.Setup(x => x.GetProductBySkuAsync("SKU")).ReturnsAsync(product);
        var service = CreateService(products.Object);
        var result = new AkeneoProductImportResult();
        var context = await service.PrepareAsync(Source(status), AkeneoEntityType.Product, Request(), result);
        await service.SynchronizeAsync(context);
        Assert.That(result.Errors, Is.Empty);
        Assert.That(product.Published, Is.EqualTo(expected));
    }

    [Test]
    public async Task Hold_prevents_template_evaluation_and_destination_writes()
    {
        var pipeline = new Mock<IAkeneoProductSyncPipeline>(MockBehavior.Strict);
        var renderer = new Mock<IAkeneoValueTemplateRenderer>(MockBehavior.Strict);
        var service = CreateService(Mock.Of<IProductService>(), pipeline.Object, renderer.Object);
        var result = new AkeneoProductImportResult();
        var context = await service.PrepareAsync(Source("hold"), AkeneoEntityType.Product, Request(), result);
        await service.RunPrepareAsync(context);
        await service.SynchronizeAsync(context);
        Assert.That(result.ActionType, Is.EqualTo(SyncItemActionType.Skipped));
        pipeline.VerifyNoOtherCalls();
        renderer.VerifyNoOtherCalls();
    }

    [Test]
    public async Task Required_template_blocks_missing_status_before_writes()
    {
        var pipeline = new Mock<IAkeneoProductSyncPipeline>(MockBehavior.Strict);
        var service = CreateService(Mock.Of<IProductService>(), pipeline.Object);
        var result = new AkeneoProductImportResult();
        var context = await service.PrepareAsync(Source(null), AkeneoEntityType.Product, Request(), result);
        Assert.That(result.Errors.Single(), Does.Contain("tokens had no value"));
        await service.RunPrepareAsync(context);
        await service.SynchronizeAsync(context);
        Assert.That(result.ActionType, Is.EqualTo(SyncItemActionType.Failed));
        pipeline.VerifyNoOtherCalls();
    }

    [TestCase("published", "true")]
    [TestCase("unpublished", "false")]
    public async Task Template_inherits_common_model_value_without_changing_leaf_payload(string status, string expected)
    {
        var api = new Mock<IAkeneoApiClient>();
        var model = Source(status); model.Code = "common";
        api.Setup(x => x.GetProductModelByCodeAsync("common", It.IsAny<CancellationToken>())).ReturnsAsync(model);
        var source = Source(null); source.Parent = "common";
        source.Values = JsonSerializer.SerializeToElement(new { });
        var rendered = await new AkeneoValueTemplateRenderer(new AkeneoProductValueResolver(), akeneoApiClient: api.Object)
            .RenderAsync(Template, Context(source));
        Assert.That(rendered.Success, Is.True);
        Assert.That(rendered.Value, Is.EqualTo(expected));
        Assert.That(source.Values.GetRawText(), Is.EqualTo("{}"));
    }

    [Test]
    public async Task Template_does_not_inherit_over_explicit_null_or_use_another_channel()
    {
        var api = new Mock<IAkeneoApiClient>(MockBehavior.Strict);
        var source = Source(null); source.Parent = "common";
        var renderer = new AkeneoValueTemplateRenderer(new AkeneoProductValueResolver(), akeneoApiClient: api.Object);
        var rendered = await renderer.RenderAsync(Template, Context(source));
        Assert.That(rendered.MissingTokens, Does.Contain("sync_status"));
        api.VerifyNoOtherCalls();
        rendered = await renderer.RenderAsync(Template, Context(Source("published", "other")));
        Assert.That(rendered.MissingTokens, Does.Contain("sync_status"));
    }

    [Test]
    public async Task Inheritance_failure_is_reported_as_template_error()
    {
        var api = new Mock<IAkeneoApiClient>();
        var source = Source(null); source.Parent = "missing";
        source.Values = JsonSerializer.SerializeToElement(new { });
        var rendered = await new AkeneoValueTemplateRenderer(new AkeneoProductValueResolver(), akeneoApiClient: api.Object)
            .RenderAsync(Template, Context(source));
        Assert.That(rendered.Success, Is.False);
        Assert.That(rendered.Errors.Single(), Does.Contain("not found"));
    }

    private static AkeneoValueTemplateContext Context(AkeneoProductDefinition source) => new()
    {
        Source = source, Locale = "en_US", Channel = "ecommerce", Currency = "USD"
    };

    private static AkeneoProductSyncService CreateService(IProductService products,
        IAkeneoProductSyncPipeline? pipeline = null, IAkeneoValueTemplateRenderer? renderer = null)
    {
        var mappings = new Mock<IAkeneoAttributeMappingService>();
        mappings.Setup(x => x.GetEffectiveMappingsAsync(It.IsAny<string>())).ReturnsAsync(new List<AkeneoAttributeMapping>
        {
            new() { MappingKey = "publication", Name = "Publication status", ValueMode = AkeneoAttributeMappingValueMode.Template,
                ValueTemplate = Template, IsRequired = true, NopTargetTypeId = (int)NopTargetType.ProductField, NopTargetKey = "Published" }
        });
        mappings.Setup(x => x.GetAllFallbackSourcesAsync()).ReturnsAsync(new List<AkeneoAttributeMappingFallbackSource>());
        var assets = new Mock<IAkeneoAssetMappingService>();
        assets.Setup(x => x.GetEffectiveMappingsAsync(It.IsAny<string>())).ReturnsAsync(new List<AkeneoAssetMapping>());
        var resolver = new AkeneoProductValueResolver();
        return new AkeneoProductSyncService(resolver, Mock.Of<IAkeneoReferenceEntityValueResolver>(),
            Mock.Of<IAkeneoValueTransformationService>(), renderer ?? new AkeneoValueTemplateRenderer(resolver),
            mappings.Object, assets.Object, Mock.Of<IAkeneoNopEntityMappingService>(), Mock.Of<IAkeneoProductSyncStateService>(), products,
            pipeline ?? new AkeneoProductSyncPipeline(new[] { new AkeneoProductCoreSynchronizer(products, Mock.Of<IRepository<Product>>()) }, Mock.Of<IEventPublisher>()),
            new AkeneoProfileRuleEvaluator(Mock.Of<IAkeneoApiClient>()),
            Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.TestData.AkeneoAttributeCatalogTestData.Create("sync_status"));
    }
}
