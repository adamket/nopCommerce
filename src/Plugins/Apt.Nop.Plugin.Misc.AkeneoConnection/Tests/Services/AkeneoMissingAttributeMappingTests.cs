using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Moq;
using Nop.Core.Domain.Catalog;
using Nop.Core.Events;
using Nop.Data;
using Nop.Services.Catalog;
using NUnit.Framework;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

[TestFixture]
public class AkeneoMissingAttributeMappingTests
{
    private Mock<IRepository<Product>> _productRepository = new();

    [TestCase(true)]
    [TestCase(false)]
    public async Task Deleted_required_attribute_preserves_publication_and_reports_skip(bool published)
    {
        var (service, products, _, _) = Create(Catalog(), Mapping());
        var product = new Product { Id = 100, Sku = "SKU", Published = published };
        products.Setup(x => x.GetProductBySkuAsync("SKU")).ReturnsAsync(product);
        var context = await Prepare(service, Request());
        var preview = new AkeneoProductMappingPreviewModel();
        await new AkeneoProductCoreSynchronizer(products.Object, Mock.Of<IRepository<Product>>()).PlanAsync(context, preview);
        Assert.That(preview.Operations.Single(x => x.Target == "Published").ChangeType, Is.EqualTo(AkeneoDryRunOperationType.Skip));
        await service.SynchronizeAsync(context);
        Assert.That(context.Result.Errors, Is.Empty);
        Assert.That(context.Result.Warnings.Single(), Does.Contain("removed_attribute"));
        Assert.That(context.MappedValues.Single().EvaluationOutcome, Is.EqualTo(AkeneoMappingEvaluationOutcome.SkippedMissingAttribute));
        Assert.That(product.Published, Is.EqualTo(published));
        _productRepository.Verify(x => x.UpdateAsync(It.IsAny<Product>(), It.IsAny<bool>()), Times.Never);
    }

    [TestCase(NopTargetType.CustomHandler)]
    [TestCase(NopTargetType.CustomProperty)]
    [TestCase(NopTargetType.ProductAttribute)]
    [TestCase(NopTargetType.SpecificationAttribute)]
    public async Task Deleted_source_is_excluded_from_downstream_mappings(NopTargetType type)
    {
        var mapping = Mapping(); mapping.NopTargetTypeId = (int)type;
        var (service, _, _, _) = Create(Catalog(), mapping);
        var context = await Prepare(service, Request());
        Assert.That(context.GetMappings(type), Is.Empty);
        Assert.That(context.GetMappings(type, includeSkipped: true).Single().IsSkipped, Is.True);
        Assert.That(context.Result.Errors, Is.Empty);
    }

    [Test]
    public async Task Existing_attribute_with_missing_product_value_still_enforces_required()
    {
        var (service, _, _, _) = Create(Catalog("removed_attribute"), Mapping());
        var context = await service.PrepareAsync(Source("{}"), AkeneoEntityType.Product, Request(), new AkeneoProductImportResult());
        Assert.That(context.Result.Errors, Has.Some.Contains("Required Akeneo source"));
        Assert.That(context.MappedValues.Single().IsSkipped, Is.False);
    }

    [Test]
    public async Task Existing_attribute_still_maps_and_deleted_payload_value_does_not_override_catalog()
    {
        var (service, _, _, _) = Create(Catalog("removed_attribute"), Mapping());
        var context = await Prepare(service, Request());
        Assert.That(context.MappedValues.Single().HasValue, Is.True);
        Assert.That(context.Result.Warnings, Is.Empty);
    }

    [TestCase("{removed_attribute}", null)]
    [TestCase("true", "removed_attribute == ready")]
    public async Task Computed_mapping_checks_template_and_condition_dependencies(string template, string? condition)
    {
        var mapping = Mapping(); mapping.ValueMode = AkeneoAttributeMappingValueMode.Template;
        mapping.ValueTemplate = template; mapping.ApplyWhen = condition;
        var (service, _, _, _) = Create(Catalog(), mapping);
        var context = await Prepare(service, Request());
        Assert.That(context.MappedValues.Single().EvaluationOutcome, Is.EqualTo(AkeneoMappingEvaluationOutcome.SkippedMissingAttribute));
        Assert.That(context.Result.Errors, Is.Empty);
    }

    [Test]
    public async Task Deleted_fallback_source_skips_mapping_before_any_value_resolution()
    {
        var mapping = Mapping(); mapping.AkeneoAttributeCode = "valid_attribute";
        var (service, _, mappings, _) = Create(Catalog("valid_attribute"), mapping);
        mappings.Setup(x => x.GetAllFallbackSourcesAsync()).ReturnsAsync(new List<AkeneoAttributeMappingFallbackSource>
        {
            new() { AttributeMappingId = mapping.Id, AkeneoAttributeCode = "removed_attribute" }
        });
        var context = await Prepare(service, Request());
        Assert.That(context.MappedValues.Single().IsSkipped, Is.True);
        Assert.That(context.Result.Warnings.Single(), Does.Contain("removed_attribute"));
    }

    [Test]
    public async Task Catalog_failure_blocks_sync_instead_of_treating_attributes_as_deleted()
    {
        var api = Catalog();
        api.Setup(x => x.GetAttributesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Connection failed"));
        var (service, _, _, pipeline) = Create(api, Mapping(), mockPipeline: true);
        var context = await Prepare(service, Request());
        await service.SynchronizeAsync(context);
        Assert.That(context.Result.Errors.Single(), Does.Contain("Cannot validate mapping sources"));
        Assert.That(context.Result.Warnings, Is.Empty);
        pipeline.VerifyNoOtherCalls();
    }

    [Test]
    public async Task Catalog_is_reused_within_run_and_refreshed_for_next_request()
    {
        var api = Catalog();
        var codes = new List<AkeneoAttributeDefinition> { new() { Code = "removed_attribute" } };
        api.Setup(x => x.GetAttributesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => codes);
        var (service, _, _, _) = Create(api, Mapping());
        var request = Request();
        Assert.That((await Prepare(service, request)).MappedValues.Single().IsSkipped, Is.False);
        codes.Clear();
        Assert.That((await Prepare(service, request)).MappedValues.Single().IsSkipped, Is.False);
        Assert.That((await Prepare(service, Request())).MappedValues.Single().IsSkipped, Is.True);
        api.Verify(x => x.GetAttributesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [TestCase("enabled")]
    [TestCase("identifier")]
    [TestCase("sku")]
    public async Task Product_root_fields_do_not_require_attribute_definitions(string code)
    {
        var mapping = Mapping(); mapping.AkeneoAttributeCode = code;
        var api = Catalog();
        var (service, _, _, _) = Create(api, mapping);
        var context = await Prepare(service, Request());
        Assert.That(context.MappedValues.Single().HasValue, Is.True);
        api.Verify(x => x.GetAttributesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static AkeneoAttributeMapping Mapping() => new()
    {
        Id = 42, MappingKey = "publication", AkeneoAttributeCode = "removed_attribute",
        NopTargetTypeId = (int)NopTargetType.ProductField, NopTargetKey = "Published", IsRequired = true
    };

    private static AkeneoProductDefinition Source(string? values = null) => new()
    {
        Identifier = "SKU", Uuid = "uuid", Family = "family", Enabled = true,
        Values = JsonSerializer.Deserialize<JsonElement>(values ?? """{"removed_attribute":[{"data":false,"locale":null,"scope":null}]}""")
    };

    private static AkeneoProductImportRequest Request() => new()
    {
        ProductFieldMissingValueBehavior = AkeneoMissingValueBehavior.ClearExisting
    };

    private static Task<AkeneoProductSyncContext> Prepare(AkeneoProductSyncService service, AkeneoProductImportRequest request) =>
        service.PrepareAsync(Source(), AkeneoEntityType.Product, request, new AkeneoProductImportResult());

    private static Mock<IAkeneoApiClient> Catalog(params string[] codes)
    {
        var api = new Mock<IAkeneoApiClient>();
        api.Setup(x => x.GetAttributesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(codes.Select(code => new AkeneoAttributeDefinition { Code = code }).ToList());
        return api;
    }

    private (AkeneoProductSyncService, Mock<IProductService>, Mock<IAkeneoAttributeMappingService>, Mock<IAkeneoProductSyncPipeline>)
        Create(Mock<IAkeneoApiClient> api, AkeneoAttributeMapping mapping, bool mockPipeline = false)
    {
        var mappings = new Mock<IAkeneoAttributeMappingService>();
        mappings.Setup(x => x.GetEffectiveMappingsAsync(It.IsAny<string>())).ReturnsAsync(new List<AkeneoAttributeMapping> { mapping });
        mappings.Setup(x => x.GetAllFallbackSourcesAsync()).ReturnsAsync(new List<AkeneoAttributeMappingFallbackSource>());
        var assets = new Mock<IAkeneoAssetMappingService>();
        assets.Setup(x => x.GetEffectiveMappingsAsync(It.IsAny<string>())).ReturnsAsync(new List<AkeneoAssetMapping>());
        _productRepository = new Mock<IRepository<Product>>();
        var products = new Mock<IProductService>();
        var pipeline = new Mock<IAkeneoProductSyncPipeline>(MockBehavior.Strict);
        var resolver = new AkeneoProductValueResolver();
        var service = new AkeneoProductSyncService(resolver, Mock.Of<IAkeneoReferenceEntityValueResolver>(),
            Mock.Of<IAkeneoValueTransformationService>(MockBehavior.Strict), new AkeneoValueTemplateRenderer(resolver),
            mappings.Object, assets.Object, Mock.Of<IAkeneoNopEntityMappingService>(), Mock.Of<IAkeneoProductSyncStateService>(),
            products.Object, mockPipeline ? pipeline.Object : new AkeneoProductSyncPipeline(new[] { new AkeneoProductCoreSynchronizer(products.Object, _productRepository.Object) }, Mock.Of<IEventPublisher>()),
            new AkeneoProfileRuleEvaluator(api.Object), api.Object);
        return (service, products, mappings, pipeline);
    }
}
