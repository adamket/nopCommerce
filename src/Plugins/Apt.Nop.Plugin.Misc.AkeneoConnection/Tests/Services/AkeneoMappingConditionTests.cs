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
using Nop.Services.Common;
using Nop.Services.Seo;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

[TestFixture]
public class AkeneoMappingConditionTests
{
    private Mock<IRepository<Product>> _productRepository = new();
    private const string Guard = "sync_status == published OR sync_status == unpublished";
    private const string ValueTemplate = "{sync_status == published ? true : false}";
    private static readonly AkeneoValueTemplateRenderer Renderer = new(new AkeneoProductValueResolver());

    private static AkeneoProductDefinition Source(string? status) => new()
    {
        Identifier = "SKU", Uuid = "uuid", Enabled = true,
        Values = JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            ["sync_status"] = new[] { new { data = status, locale = (string?)null, scope = "ecommerce" } }
        })
    };

    private static AkeneoValueTemplateContext Context(string? status) => new()
    {
        Source = Source(status), Locale = "en_US", Channel = "ecommerce", Currency = "USD"
    };

    [TestCase("published", true)]
    [TestCase("unpublished", true)]
    [TestCase("hold", false)]
    [TestCase("draft", false)]
    public async Task Whitelist_guard_only_matches_publication_states(string status, bool expected)
    {
        var result = await AkeneoMappingCondition.EvaluateAsync(Guard, Renderer, Context(status));
        Assert.That(result.Success, Is.True);
        Assert.That(result.Matches, Is.EqualTo(expected));
    }

    [TestCase("sync_status == hold OR sync_status == unpublished AND enabled == false", true)]
    [TestCase("(sync_status == hold OR sync_status == unpublished) AND enabled == false", false)]
    [TestCase("sync_status != published && enabled == true", true)]
    [TestCase("sync_status == 'hold' or (sync_status == published AND enabled != true)", true)]
    [TestCase("sync_status == \"hold OR published\"", false)]
    public async Task Logical_conditions_have_precedence_parentheses_and_quoted_literals(string condition, bool expected)
    {
        Assert.That(AkeneoMappingCondition.Validate(condition, Renderer).Success, Is.True);
        var result = await AkeneoMappingCondition.EvaluateAsync(condition, Renderer, Context("hold"));
        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(result.Matches, Is.EqualTo(expected));
    }

    [TestCase("sync_status = published")]
    [TestCase("sync_status == published OR")]
    [TestCase("(sync_status == published")]
    [TestCase("sync_status == 'published")]
    [TestCase("sync_status == published ? true : false")]
    [TestCase("{sync_status}")]
    [TestCase("System.IO.File.Delete('x')")]
    [TestCase("sync_status == published AND ()")]
    public void Malformed_or_unsupported_conditions_are_rejected(string condition) =>
        Assert.That(AkeneoMappingCondition.Validate(condition, Renderer).Success, Is.False);

    [Test]
    public void Validation_tracks_condition_dependencies()
    {
        var result = AkeneoMappingCondition.Validate("sync_status == published AND approval != pending", Renderer);
        Assert.That(result.ReferencedAttributeCodes, Is.EquivalentTo(new[] { "sync_status", "approval" }));
    }

    [Test]
    public async Task Missing_values_block_even_when_other_or_branch_matches()
    {
        var result = await AkeneoMappingCondition.EvaluateAsync("sync_status == published OR approval == approved", Renderer, Context("published"));
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("approval"));
    }

    [Test]
    public async Task Inherited_and_channel_specific_condition_values_use_template_resolution()
    {
        var api = new Mock<IAkeneoApiClient>();
        var parent = Source("published"); parent.Code = "model";
        api.Setup(x => x.GetProductModelByCodeAsync("model", It.IsAny<CancellationToken>())).ReturnsAsync(parent);
        var source = Source(null); source.Parent = "model"; source.Values = JsonSerializer.SerializeToElement(new { });
        var renderer = new AkeneoValueTemplateRenderer(new AkeneoProductValueResolver(), akeneoApiClient: api.Object);
        var result = await AkeneoMappingCondition.EvaluateAsync(Guard, renderer,
            new AkeneoValueTemplateContext { Source = source, Channel = "ecommerce", Locale = "en_US" });
        Assert.That(result.Success && result.Matches, Is.True);
        result = await AkeneoMappingCondition.EvaluateAsync(Guard, renderer,
            new AkeneoValueTemplateContext { Source = source, Channel = "other", Locale = "en_US" });
        Assert.That(result.Success, Is.False);
    }

    [TestCase("published", true)]
    [TestCase("unpublished", false)]
    public async Task Allowed_condition_applies_the_computed_publication(string status, bool expected)
    {
        var product = new Product { Id = 100, Sku = "SKU", Published = !expected };
        var (service, _) = CreateService(product);
        var context = await Prepare(service, status);
        await service.SynchronizeAsync(context);
        Assert.That(context.Result.Success, Is.True);
        Assert.That(product.Published, Is.EqualTo(expected));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task False_condition_preserves_publication_despite_clear_missing_and_enabled_restore(bool published)
    {
        var product = new Product { Id = 100, Sku = "SKU", Published = published };
        var (service, products) = CreateService(product);
        var context = await Prepare(service, "hold");
        Assert.That(context.MappedValues.Single().EvaluationOutcome, Is.EqualTo(AkeneoMappingEvaluationOutcome.SkippedByCondition));
        var preview = new AkeneoProductMappingPreviewModel();
        await new AkeneoProductCoreSynchronizer(products.Object, Mock.Of<IRepository<Product>>()).PlanAsync(context, preview);
        Assert.That(preview.Operations.Where(x => x.Target == "Published").Single().ChangeType, Is.EqualTo(AkeneoDryRunOperationType.Skip));
        await service.SynchronizeAsync(context);
        Assert.That(context.Result.Errors, Is.Empty);
        Assert.That(product.Published, Is.EqualTo(published));
        _productRepository.Verify(x => x.UpdateAsync(It.IsAny<Product>(), It.IsAny<bool>()), Times.Never);
    }

    [Test]
    public async Task Skipped_publication_on_new_product_does_not_inherit_enabled()
    {
        var (service, products) = CreateService(null);
        var context = await Prepare(service, "hold");
        await service.SynchronizeAsync(context);
        products.Verify(x => x.InsertProductAsync(It.Is<Product>(p => !p.Published)), Times.Once);
    }

    [Test]
    public async Task False_condition_bypasses_required_template_and_transform()
    {
        var transforms = new Mock<IAkeneoValueTransformationService>(MockBehavior.Strict);
        var (service, _) = CreateService(new Product { Id = 100, Sku = "SKU" },
            template: "{missing_required_value}", transforms: transforms.Object);
        var context = await Prepare(service, "hold");
        await service.SynchronizeAsync(context);
        Assert.That(context.Result.Errors, Is.Empty);
        transforms.VerifyNoOtherCalls();
    }

    [TestCase(null)]
    [TestCase("invalid")]
    public async Task Missing_or_invalid_condition_blocks_optional_mapping_and_all_writes(string? condition)
    {
        var (service, products) = CreateService(new Product { Id = 100, Sku = "SKU" },
            guard: condition == null ? Guard : condition, required: false);
        var context = await Prepare(service, condition == null ? null : "published");
        Assert.That(context.MappedValues.Single().EvaluationOutcome, Is.EqualTo(AkeneoMappingEvaluationOutcome.Failed));
        Assert.That(context.Result.Errors, Is.Not.Empty);
        await service.SynchronizeAsync(context);
        _productRepository.Verify(x => x.UpdateAsync(It.IsAny<Product>(), It.IsAny<bool>()), Times.Never);
        products.Verify(x => x.InsertProductAsync(It.IsAny<Product>()), Times.Never);
    }

    [Test]
    public async Task Blank_condition_preserves_existing_computed_mapping_behavior()
    {
        var product = new Product { Id = 100, Sku = "SKU", Published = true };
        var (service, _) = CreateService(product, guard: "");
        var context = await Prepare(service, "hold");
        await service.SynchronizeAsync(context);
        Assert.That(product.Published, Is.False);
        Assert.That(context.MappedValues.Single().SkippedByCondition, Is.False);
    }

    [Test]
    public async Task A_skipped_mapping_does_not_stop_other_product_field_updates()
    {
        var products = new Mock<IProductService>();
        var product = new Product { Id = 100, Published = true, Name = "Old" };
        var skipped = Skipped(NopTargetType.ProductField, "Published");
        var context = new AkeneoProductSyncContext
        {
            Source = Source("hold"), Request = new AkeneoProductImportRequest(), Result = new AkeneoProductImportResult(),
            ExistingProduct = product,
            MappedValues = new[] { skipped, new AkeneoResolvedMappedValue
            {
                Mapping = new AkeneoAttributeMapping { NopTargetTypeId = (int)NopTargetType.ProductField, NopTargetKey = "Name" },
                HasValue = true, Value = new AkeneoResolvedProductValue { DisplayValue = "New" }
            } }
        };
        await new AkeneoProductCoreSynchronizer(products.Object, Mock.Of<IRepository<Product>>()).SynchronizeAsync(context);
        Assert.That(product.Published, Is.True);
        Assert.That(product.Name, Is.EqualTo("New"));
    }

    [Test]
    public async Task Skipped_seo_and_custom_properties_bypass_clearing_and_slug_fallback()
    {
        var products = new Mock<IProductService>(MockBehavior.Strict);
        var urls = new Mock<IUrlRecordService>(MockBehavior.Strict);
        var attributes = new Mock<IGenericAttributeService>(MockBehavior.Strict);
        var product = new Product { Id = 100, MetaTitle = "Keep" };
        var context = new AkeneoProductSyncContext
        {
            Source = Source("hold"), Result = new AkeneoProductImportResult(), ExistingProduct = product, Product = product,
            Request = new AkeneoProductImportRequest
            {
                SeoFieldMissingValueBehavior = AkeneoMissingValueBehavior.ClearExisting,
                CustomPropertyMissingValueBehavior = AkeneoMissingValueBehavior.ClearExisting
            },
            MappedValues = new[] { Skipped(NopTargetType.SeoField, "SeName"), Skipped(NopTargetType.SeoField, "MetaTitle"),
                Skipped(NopTargetType.CustomProperty, "keep") }
        };
        await new AkeneoProductSeoSynchronizer(products.Object, Mock.Of<IRepository<Product>>(), urls.Object).SynchronizeAsync(context);
        await new AkeneoProductCustomPropertySynchronizer(attributes.Object).SynchronizeAsync(context);
        Assert.That(product.MetaTitle, Is.EqualTo("Keep"));
        products.VerifyNoOtherCalls(); urls.VerifyNoOtherCalls(); attributes.VerifyNoOtherCalls();
    }

    private static AkeneoResolvedMappedValue Skipped(NopTargetType type, string key) => new()
    {
        Mapping = new AkeneoAttributeMapping { NopTargetTypeId = (int)type, NopTargetKey = key, IsRequired = true },
        EvaluationOutcome = AkeneoMappingEvaluationOutcome.SkippedByCondition, ConditionDetail = "Skipped by condition"
    };

    private static Task<AkeneoProductSyncContext> Prepare(AkeneoProductSyncService service, string? status) =>
        service.PrepareAsync(Source(status), AkeneoEntityType.Product,
            new AkeneoProductImportRequest
            {
                SyncProfileId = 7, SyncRunRecordId = 23, Locale = "en_US", Channel = "ecommerce",
                ProductFieldMissingValueBehavior = AkeneoMissingValueBehavior.ClearExisting
                // No profile hold rule: exercise mapping-only preservation.
            }, new AkeneoProductImportResult());

    private (AkeneoProductSyncService, Mock<IProductService>) CreateService(Product? product,
        string guard = Guard, string template = ValueTemplate, bool required = true,
        IAkeneoValueTransformationService? transforms = null)
    {
        _productRepository = new Mock<IRepository<Product>>();
        var products = new Mock<IProductService>();
        products.Setup(x => x.GetProductBySkuAsync("SKU")).ReturnsAsync(product!);
        var mappings = new Mock<IAkeneoAttributeMappingService>();
        mappings.Setup(x => x.GetEffectiveMappingsAsync(It.IsAny<string>())).ReturnsAsync(new List<AkeneoAttributeMapping>
        {
            new() { MappingKey = "published", Name = "Publication", ValueMode = AkeneoAttributeMappingValueMode.Template,
                ApplyWhen = guard, ValueTemplate = template, NopTargetTypeId = (int)NopTargetType.ProductField,
                NopTargetKey = "Published", IsRequired = required, TransformRuleJson = transforms == null ? null : "{}" }
        });
        mappings.Setup(x => x.GetAllFallbackSourcesAsync()).ReturnsAsync(new List<AkeneoAttributeMappingFallbackSource>());
        var assets = new Mock<IAkeneoAssetMappingService>();
        assets.Setup(x => x.GetEffectiveMappingsAsync(It.IsAny<string>())).ReturnsAsync(new List<AkeneoAssetMapping>());
        var states = new Mock<IAkeneoProductSyncStateService>();
        states.Setup(x => x.GetBySourceAsync(7, AkeneoEntityType.Product, "SKU", "uuid")).ReturnsAsync(new AkeneoProductSyncState
        {
            NopProductId = product?.Id ?? 0, LifecycleStatusId = (int)AkeneoProductLifecycleStatus.Unpublished
        });
        var service = new AkeneoProductSyncService(new AkeneoProductValueResolver(), Mock.Of<IAkeneoReferenceEntityValueResolver>(),
            transforms ?? Mock.Of<IAkeneoValueTransformationService>(), Renderer, mappings.Object, assets.Object,
            Mock.Of<IAkeneoNopEntityMappingService>(), states.Object, products.Object,
            new AkeneoProductSyncPipeline(new[] { new AkeneoProductCoreSynchronizer(products.Object, _productRepository.Object) }, Mock.Of<IEventPublisher>()),
            new AkeneoProfileRuleEvaluator(Mock.Of<IAkeneoApiClient>()),
            Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.TestData.AkeneoAttributeCatalogTestData.Create("sync_status", "missing_required_value"));
        return (service, products);
    }
}
