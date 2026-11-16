using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Moq;
using Nop.Core.Domain.Catalog;
using Nop.Data;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

/// <summary>
/// The sync never creates anything for an Akeneo product that is not 100%
/// complete for the profile's channel and primary locale; products already in
/// nopCommerce keep syncing.
/// </summary>
[TestFixture]
public class AkeneoIncompleteProductRuleTests
{
    private const string Channel = "ecommerce";
    private const string Locale = "en_US";

    private static AkeneoProductDefinition Source(int? percent, string locale = Locale) => new()
    {
        Uuid = "uuid", Identifier = "SKU", Family = "trees", Enabled = true,
        Values = JsonSerializer.SerializeToElement(new Dictionary<string, object>()),
        Completenesses = percent == null
            ? null
            : [new AkeneoCompleteness { Scope = Channel, Locale = locale, Data = percent }]
    };

    [Test]
    public void Incomplete_product_is_blocked_with_its_percentage()
    {
        var reason = AkeneoIncompleteProductRule.GetCreationBlockReason(Source(80), Channel, Locale);

        Assert.That(reason, Does.StartWith("Not imported: the product is 80% complete"));
        Assert.That(reason, Does.Contain("'ecommerce'").And.Contain("'en_US'"));
    }

    [TestCase(100)]
    [TestCase(null)]
    public void Complete_or_unrated_product_is_not_blocked(int? percent)
    {
        Assert.That(AkeneoIncompleteProductRule.GetCreationBlockReason(Source(percent), Channel, Locale), Is.Null);
    }

    [Test]
    public void Completeness_for_another_locale_does_not_count()
    {
        Assert.That(AkeneoIncompleteProductRule.GetCreationBlockReason(Source(50, "fr_FR"), Channel, Locale), Is.Null);
    }

    [Test]
    public async Task Core_does_not_create_an_incomplete_product_and_warns()
    {
        var products = new Mock<IProductService>();
        var context = CoreContext(Source(60), existing: null);

        await new AkeneoProductCoreSynchronizer(products.Object, Mock.Of<IRepository<Product>>(),
            Mock.Of<IAkeneoFamilyMappingService>()).SynchronizeAsync(context);

        products.Verify(p => p.InsertProductAsync(It.IsAny<Product>()), Times.Never);
        Assert.That(context.StopProcessing, Is.True);
        Assert.That(context.Result.Warnings, Has.Some.StartWith("Not imported: the product is 60% complete"));
    }

    [Test]
    public async Task Core_dry_run_reports_the_block()
    {
        var context = CoreContext(Source(60), existing: null);
        var model = new AkeneoProductMappingPreviewModel();

        await new AkeneoProductCoreSynchronizer(Mock.Of<IProductService>(), Mock.Of<IRepository<Product>>(),
            Mock.Of<IAkeneoFamilyMappingService>()).PlanAsync(context, model);

        Assert.That(model.ImportAllowedByProfile, Is.False);
        Assert.That(model.ImportBlockReason, Does.StartWith("Not imported"));
    }

    [Test]
    public async Task Core_keeps_updating_an_existing_incomplete_product()
    {
        var existing = new Product { Id = 5, Sku = "SKU", Name = "SKU" };
        var context = CoreContext(Source(60), existing);

        await new AkeneoProductCoreSynchronizer(Mock.Of<IProductService>(), Mock.Of<IRepository<Product>>(),
            Mock.Of<IAkeneoFamilyMappingService>()).SynchronizeAsync(context);

        Assert.That(context.StopProcessing, Is.False);
        Assert.That(context.Product, Is.SameAs(existing));
        Assert.That(context.Result.Warnings, Has.None.StartWith("Not imported"));
    }

    [Test]
    public async Task Core_creates_a_complete_product()
    {
        var products = new Mock<IProductService>();
        var context = CoreContext(Source(100), existing: null);

        await new AkeneoProductCoreSynchronizer(products.Object, Mock.Of<IRepository<Product>>(),
            Mock.Of<IAkeneoFamilyMappingService>()).SynchronizeAsync(context);

        products.Verify(p => p.InsertProductAsync(It.IsAny<Product>()), Times.Once);
    }

    [Test]
    public async Task Batch_skips_a_new_incomplete_product_before_writing_anything()
    {
        var api = new Mock<IAkeneoApiClient>();
        var states = new Mock<IAkeneoProductSyncStateService>();
        var products = new Mock<IProductService>();
        var syncService = new Mock<IAkeneoProductSyncService>(MockBehavior.Strict);
        var source = Source(40);
        source.Parent = "model";
        api.Setup(x => x.GetProductByUuidAsync("uuid", It.IsAny<CancellationToken>())).ReturnsAsync(source);

        var result = await Batch(api, states, products, syncService).SyncProductByUuidAsync(BatchRequest());

        Assert.That(result.ActionType, Is.EqualTo(SyncItemActionType.Skipped));
        Assert.That(result.Errors, Is.Empty);
        Assert.That(result.Warnings, Has.Some.StartWith("Not imported: the product is 40% complete"));
        // Stopped before the product or its parent was prepared or written.
        syncService.VerifyNoOtherCalls();
        products.Verify(p => p.InsertProductAsync(It.IsAny<Product>()), Times.Never);
    }

    [Test]
    public async Task Batch_processes_an_incomplete_product_whose_sku_already_exists()
    {
        var api = new Mock<IAkeneoApiClient>();
        var states = new Mock<IAkeneoProductSyncStateService>();
        var products = new Mock<IProductService>();
        var syncService = new Mock<IAkeneoProductSyncService>();
        api.Setup(x => x.GetProductByUuidAsync("uuid", It.IsAny<CancellationToken>())).ReturnsAsync(Source(40));
        products.Setup(p => p.GetProductBySkuAsync("SKU")).ReturnsAsync(new Product { Id = 5, Sku = "SKU" });

        AkeneoProductImportResult? result = null;
        try
        {
            result = await Batch(api, states, products, syncService).SyncProductByUuidAsync(BatchRequest());
        }
        catch (Exception)
        {
            // The loose mocks cannot finish a real write; reaching it is the point.
        }

        Assert.That(result?.Warnings ?? [], Has.None.StartWith("Not imported"));
        Assert.That(syncService.Invocations, Is.Not.Empty);
    }

    private static AkeneoProductSyncContext CoreContext(AkeneoProductDefinition source, Product? existing) => new()
    {
        Source = source,
        SourceEntityType = AkeneoEntityType.Product,
        Request = new AkeneoProductImportRequest { Channel = Channel, Locale = Locale },
        Result = new AkeneoProductImportResult(),
        SourceCode = "SKU",
        Sku = "SKU",
        ProductKey = "SKU",
        MappingFamilyCode = "trees",
        ExistingProduct = existing
    };

    private static AkeneoProductImportRequest BatchRequest() => new()
    {
        SyncProfileId = 7, SyncRunRecordId = 23, AkeneoProductUuid = "uuid",
        Locale = Locale, Channel = Channel, Currency = "USD", RunMode = AkeneoRunMode.Full
    };

    private static AkeneoProductBatchSyncService Batch(
        Mock<IAkeneoApiClient> api,
        Mock<IAkeneoProductSyncStateService> states,
        Mock<IProductService> products,
        Mock<IAkeneoProductSyncService> syncService) =>
        new(api.Object, Mock.Of<IAkeneoProductValueResolver>(),
            Mock.Of<IAkeneoNopEntityMappingService>(),
            Mock.Of<IAkeneoSyncItemLogService>(), products.Object,
            Mock.Of<IAkeneoFamilyMappingService>(),
            new AkeneoProductModelHierarchyResolver(),
            Mock.Of<IAkeneoVariantRelationshipService>(),
            Mock.Of<IAkeneoLeafRepresentationClassifier>(),
            syncService.Object, states.Object,
            Mock.Of<IAkeneoVariantRepresentationCleanupService>(),
            Mock.Of<IAkeneoCatalogReconciliationService>(),
            Mock.Of<IAkeneoSyncLeaseService>(), Mock.Of<IAkeneoProductModelDeltaFanOutService>(),
            Mock.Of<IAkeneoDryRunChangeAnalyzer>(),
            Mock.Of<IAkeneoAssetMappingService>(),
            Mock.Of<IAkeneoAssetResolver>(), new AkeneoProfileRuleEvaluator(api.Object));
}
