using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Seo;
using Nop.Data;
using Nop.Services.Catalog;
using Nop.Services.Seo;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

[TestFixture]
public class AkeneoProductSeoSlugReclaimTests
{
    private Mock<IProductService> _productService = null!;
    private Mock<IUrlRecordService> _urlRecordService = null!;
    private Mock<IRepository<UrlRecord>> _urlRecordRepository = null!;
    private List<UrlRecord> _urlRecords = null!;

    [SetUp]
    public void SetUp()
    {
        _productService = new Mock<IProductService>();
        _urlRecordService = new Mock<IUrlRecordService>();
        _urlRecordRepository = new Mock<IRepository<UrlRecord>>();
        _urlRecords = new List<UrlRecord>();

        _urlRecordRepository
            .Setup(repository => repository.Table)
            .Returns(() => _urlRecords.AsQueryable());
        _urlRecordService
            .Setup(service => service.GetSeNameAsync("red-maple", It.IsAny<bool>(), It.IsAny<bool>()))
            .ReturnsAsync("red-maple");
        _urlRecordService
            .Setup(service => service.ValidateSeNameAsync(
                It.IsAny<Product>(), "red-maple", It.IsAny<string>(), true))
            .ReturnsAsync("red-maple-2");
    }

    [Test]
    public async Task Slug_held_by_deleted_product_is_moved_to_the_synced_product()
    {
        var held = HeldBy(5, deleted: true);

        var context = Context(new Product { Id = 10, Name = "Red Maple" });
        await Synchronizer().SynchronizeAsync(context);

        _urlRecordService.Verify(service => service.DeleteUrlRecordsAsync(
            It.Is<IList<UrlRecord>>(records => records.Single() == held)), Times.Once);
        _urlRecordService.Verify(service => service.SaveSlugAsync(
            context.Product, "red-maple", 0), Times.Once);
        Assert.Multiple(() =>
        {
            Assert.That(context.Result.Warnings, Is.Empty);
            Assert.That(context.Result.Messages, Has.Some.Contains("deleted nopCommerce product ID 5"));
        });
    }

    [Test]
    public async Task Slug_held_by_live_product_fails_the_product()
    {
        var held = HeldBy(5, deleted: false);
        _urlRecordService
            .Setup(service => service.GetBySlugAsync("red-maple"))
            .ReturnsAsync(held);

        var context = Context(new Product { Id = 10, Name = "Red Maple" });
        await Synchronizer().SynchronizeAsync(context);

        _urlRecordService.Verify(service => service.DeleteUrlRecordsAsync(
            It.IsAny<IList<UrlRecord>>()), Times.Never);
        _urlRecordService.Verify(service => service.SaveSlugAsync(
            It.IsAny<Product>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        Assert.Multiple(() =>
        {
            Assert.That(context.Result.Success, Is.False);
            Assert.That(context.Result.Errors.Single(), Does.Contain("'red-maple'")
                .And.Contain("product 'Live 5' (ID 5)")
                .And.Contain("'red-maple-2'"));
        });
    }

    [Test]
    public async Task Slug_held_by_another_entity_type_is_not_reclaimed()
    {
        var held = new UrlRecord { Id = 1, EntityId = 5, EntityName = nameof(Category), Slug = "red-maple", IsActive = true };
        _urlRecords.Add(held);
        _urlRecordService
            .Setup(service => service.GetBySlugAsync("red-maple"))
            .ReturnsAsync(held);

        var context = Context(new Product { Id = 10, Name = "Red Maple" });
        await Synchronizer().SynchronizeAsync(context);

        _urlRecordService.Verify(service => service.DeleteUrlRecordsAsync(
            It.IsAny<IList<UrlRecord>>()), Times.Never);
        _urlRecordService.Verify(service => service.SaveSlugAsync(
            It.IsAny<Product>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        Assert.That(context.Result.Errors.Single(), Does.Contain("Category ID 5"));
    }

    [Test]
    public async Task Slug_generated_from_name_fails_a_new_product_when_taken()
    {
        _urlRecordService
            .Setup(service => service.GetSeNameAsync("Red Maple", It.IsAny<bool>(), It.IsAny<bool>()))
            .ReturnsAsync("red-maple");
        _urlRecordService
            .Setup(service => service.ValidateSeNameAsync(
                It.IsAny<Product>(), string.Empty, "Red Maple", true))
            .ReturnsAsync("red-maple-2");

        var context = Context(null, withSlugMapping: false);
        context.PlanningState.ProposedProductName = "Red Maple";
        context.Product = new Product { Name = "Red Maple" };
        var model = new AkeneoProductMappingPreviewModel();
        await Synchronizer().PlanAsync(context, model);

        Assert.That(model.Errors.Single(), Does.Contain("generated from the product name")
            .And.Contain("reserved by nopCommerce"));
    }

    [Test]
    public async Task Slug_with_only_disallowed_characters_is_saved_with_a_warning()
    {
        _urlRecordService
            .Setup(service => service.GetSeNameAsync("Red Maple!", It.IsAny<bool>(), It.IsAny<bool>()))
            .ReturnsAsync("red-maple");
        _urlRecordService
            .Setup(service => service.ValidateSeNameAsync(
                It.IsAny<Product>(), "Red Maple!", It.IsAny<string>(), true))
            .ReturnsAsync("red-maple");

        var context = Context(new Product { Id = 10, Name = "Red Maple" });
        context.MappedValues[0].Value.DisplayValue = "Red Maple!";
        await Synchronizer().SynchronizeAsync(context);

        _urlRecordService.Verify(service => service.SaveSlugAsync(
            context.Product, "red-maple", 0), Times.Once);
        Assert.Multiple(() =>
        {
            Assert.That(context.Result.Success, Is.True);
            Assert.That(context.Result.Warnings, Has.Some.Contains("characters nopCommerce does not allow"));
        });
    }

    [Test]
    public async Task Dry_run_previews_the_reclaimed_slug_without_releasing_it()
    {
        HeldBy(5, deleted: true);

        var context = Context(new Product { Id = 10, Name = "Red Maple" });
        var model = new AkeneoProductMappingPreviewModel();
        await Synchronizer().PlanAsync(context, model);

        _urlRecordService.Verify(service => service.DeleteUrlRecordsAsync(
            It.IsAny<IList<UrlRecord>>()), Times.Never);
        Assert.That(model.Operations, Has.Some.Matches<AkeneoDryRunOperationPreviewModel>(
            operation => operation.Target == "SeName" &&
                         operation.ProposedValue == "red-maple"));
    }

    private UrlRecord HeldBy(int productId, bool deleted)
    {
        var record = new UrlRecord
        {
            Id = 1,
            EntityId = productId,
            EntityName = nameof(Product),
            Slug = "red-maple",
            IsActive = true
        };
        _urlRecords.Add(record);
        _productService
            .Setup(service => service.GetProductByIdAsync(productId))
            .ReturnsAsync(new Product { Id = productId, Name = $"Live {productId}", Deleted = deleted });

        return record;
    }

    private AkeneoProductSeoSynchronizer Synchronizer()
    {
        return new AkeneoProductSeoSynchronizer(
            _productService.Object,
            Mock.Of<IRepository<Product>>(),
            _urlRecordService.Object,
            urlRecordRepository: _urlRecordRepository.Object);
    }

    private static AkeneoProductSyncContext Context(Product product, bool withSlugMapping = true)
    {
        var source = new AkeneoProductDefinition
        {
            Uuid = "uuid-1",
            Identifier = "SKU-1",
            Family = "family",
            Enabled = true,
            Values = JsonSerializer.SerializeToElement(new Dictionary<string, object>())
        };

        return new AkeneoProductSyncContext
        {
            Source = source,
            SourceEntityType = AkeneoEntityType.Product,
            Request = new AkeneoProductImportRequest
            {
                SyncProfileId = 7,
                SyncRunRecordId = 20,
                SeoFieldMissingValueBehavior = AkeneoMissingValueBehavior.ClearExisting
            },
            Result = new AkeneoProductImportResult(),
            MappedValues = !withSlugMapping ? [] :
            [
                new AkeneoResolvedMappedValue
                {
                    Mapping = new AkeneoAttributeMapping
                    {
                        AkeneoAttributeCode = "url_slug",
                        NopTargetTypeId = (int)NopTargetType.SeoField,
                        NopTargetKey = "SeName"
                    },
                    HasValue = true,
                    Value = new AkeneoResolvedProductValue
                    {
                        AttributeCode = "url_slug",
                        DisplayValue = "red-maple",
                        RawData = JsonSerializer.SerializeToElement("red-maple")
                    },
                    ResolvedSourceDisplayName = "url_slug"
                }
            ],
            SourceCode = source.Identifier,
            SourceUuid = source.Uuid,
            MappingFamilyCode = source.Family,
            MappingEntityScope = AkeneoAttributeMappingEntityScope.StandaloneProduct,
            ProductKey = source.Identifier,
            Sku = source.Identifier,
            ExistingProduct = product,
            Product = product
        };
    }
}
