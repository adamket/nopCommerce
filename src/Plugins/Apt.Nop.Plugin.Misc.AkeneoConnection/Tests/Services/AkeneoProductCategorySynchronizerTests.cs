using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.TestData;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

[TestFixture]
public class AkeneoProductCategorySynchronizerTests
{
    private Mock<ICategoryService> _categoryService = null!;
    private Mock<IAkeneoNopEntityMappingService> _entityMappingService = null!;
    private Mock<IAkeneoManagedRelationService> _managedRelationService = null!;
    private AkeneoProductCategorySynchronizer _synchronizer = null!;

    [SetUp]
    public void SetUp()
    {
        _categoryService = new Mock<ICategoryService>(MockBehavior.Strict);
        _entityMappingService = new Mock<IAkeneoNopEntityMappingService>(MockBehavior.Strict);
        _managedRelationService = new Mock<IAkeneoManagedRelationService>(MockBehavior.Strict);
        _synchronizer = new AkeneoProductCategorySynchronizer(
            _categoryService.Object,
            _entityMappingService.Object,
            _managedRelationService.Object);
    }

    [Test]
    public async Task Merge_adds_missing_categories_and_preserves_existing_stale_categories()
    {
        var context = Context(AkeneoCollectionSyncMode.Merge, "shade");
        var existingManual = new ProductCategory { Id = 11, ProductId = 10, CategoryId = 900 };
        SetupMappedCategory("shade", 100);
        _categoryService
            .Setup(service => service.GetProductCategoriesByProductIdAsync(10, true))
            .ReturnsAsync(new List<ProductCategory> { existingManual });
        ProductCategory? inserted = null;
        _categoryService
            .Setup(service => service.InsertProductCategoryAsync(It.IsAny<ProductCategory>()))
            .Callback<ProductCategory>(mapping =>
            {
                mapping.Id = 12;
                inserted = mapping;
            })
            .Returns(Task.CompletedTask);
        _managedRelationService
            .Setup(service => service.UpsertAsync(
                7, 20, 10, AkeneoManagedRelationType.ProductCategory, 12, null, "100"))
            .ReturnsAsync(new AkeneoManagedRelation());

        await _synchronizer.SynchronizeAsync(context);

        Assert.Multiple(() =>
        {
            Assert.That(inserted!.CategoryId, Is.EqualTo(100));
            Assert.That(context.HasChanges, Is.True);
        });
        _categoryService.Verify(
            service => service.DeleteProductCategoryAsync(It.IsAny<ProductCategory>()),
            Times.Never);
    }

    [Test]
    public async Task ReplaceManaged_removes_only_stale_managed_categories()
    {
        var context = Context(AkeneoCollectionSyncMode.ReplaceManaged, "desired");
        var desired = new ProductCategory { Id = 1, ProductId = 10, CategoryId = 100 };
        var staleManaged = new ProductCategory { Id = 2, ProductId = 10, CategoryId = 200 };
        var staleManual = new ProductCategory { Id = 3, ProductId = 10, CategoryId = 300 };
        SetupMappedCategory("desired", 100);
        _categoryService
            .Setup(service => service.GetProductCategoriesByProductIdAsync(10, true))
            .ReturnsAsync(new List<ProductCategory> { desired, staleManaged, staleManual });
        var managedRelation = new AkeneoManagedRelation
        {
            Id = 50,
            NopRelationEntityId = staleManaged.Id
        };
        _managedRelationService
            .Setup(service => service.GetByProductAsync(
                7, 10, AkeneoManagedRelationType.ProductCategory, null))
            .ReturnsAsync(new List<AkeneoManagedRelation> { managedRelation });
        _categoryService
            .Setup(service => service.DeleteProductCategoryAsync(staleManaged))
            .Returns(Task.CompletedTask);
        _managedRelationService
            .Setup(service => service.DeleteAsync(managedRelation))
            .Returns(Task.CompletedTask);

        await _synchronizer.SynchronizeAsync(context);

        _categoryService.Verify(service => service.DeleteProductCategoryAsync(staleManaged), Times.Once);
        _categoryService.Verify(service => service.DeleteProductCategoryAsync(staleManual), Times.Never);
        Assert.That(context.HasChanges, Is.True);
    }

    [Test]
    public async Task ReplaceAll_removes_all_stale_categories_including_manual_relationships()
    {
        var context = Context(AkeneoCollectionSyncMode.ReplaceAll, "desired");
        var desired = new ProductCategory { Id = 1, ProductId = 10, CategoryId = 100 };
        var staleManaged = new ProductCategory { Id = 2, ProductId = 10, CategoryId = 200 };
        var staleManual = new ProductCategory { Id = 3, ProductId = 10, CategoryId = 300 };
        SetupMappedCategory("desired", 100);
        _categoryService
            .Setup(service => service.GetProductCategoriesByProductIdAsync(10, true))
            .ReturnsAsync(new List<ProductCategory> { desired, staleManaged, staleManual });
        _categoryService
            .Setup(service => service.DeleteProductCategoryAsync(staleManaged))
            .Returns(Task.CompletedTask);
        _categoryService
            .Setup(service => service.DeleteProductCategoryAsync(staleManual))
            .Returns(Task.CompletedTask);

        await _synchronizer.SynchronizeAsync(context);

        _categoryService.Verify(service => service.DeleteProductCategoryAsync(staleManaged), Times.Once);
        _categoryService.Verify(service => service.DeleteProductCategoryAsync(staleManual), Times.Once);
    }

    [Test]
    public async Task ReplaceManaged_skips_all_removal_when_desired_state_is_incomplete()
    {
        var context = Context(AkeneoCollectionSyncMode.ReplaceManaged, "missing");
        var current = new ProductCategory { Id = 2, ProductId = 10, CategoryId = 200 };
        _entityMappingService
            .Setup(service => service.GetMappedNopEntityIdByAkeneoCodeAsync(
                AkeneoEntityType.Category, "missing", NopEntityType.Category))
            .ReturnsAsync((int?)null);
        _categoryService
            .Setup(service => service.GetProductCategoriesByProductIdAsync(10, true))
            .ReturnsAsync(new List<ProductCategory> { current });

        await _synchronizer.SynchronizeAsync(context);

        Assert.That(context.Result.Warnings.Any(warning => warning.Contains("removal was skipped", StringComparison.OrdinalIgnoreCase)), Is.True);
        _managedRelationService.Verify(
            service => service.GetByProductAsync(
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<AkeneoManagedRelationType>(),
                It.IsAny<string>()),
            Times.Never);
        _categoryService.Verify(
            service => service.DeleteProductCategoryAsync(It.IsAny<ProductCategory>()),
            Times.Never);
    }

    [Test]
    public async Task ReplaceManaged_without_profile_does_not_remove_categories()
    {
        var context = Context(AkeneoCollectionSyncMode.ReplaceManaged, "desired");
        context.Request.SyncProfileId = null;
        var current = new ProductCategory { Id = 2, ProductId = 10, CategoryId = 200 };
        SetupMappedCategory("desired", 100);
        _categoryService
            .Setup(service => service.GetProductCategoriesByProductIdAsync(10, true))
            .ReturnsAsync(new List<ProductCategory> { current });
        _categoryService
            .Setup(service => service.InsertProductCategoryAsync(It.IsAny<ProductCategory>()))
            .Returns(Task.CompletedTask);

        await _synchronizer.SynchronizeAsync(context);

        Assert.That(context.Result.Warnings.Any(warning => warning.Contains("requires a sync profile", StringComparison.OrdinalIgnoreCase)), Is.True);
        _categoryService.Verify(
            service => service.DeleteProductCategoryAsync(It.IsAny<ProductCategory>()),
            Times.Never);
    }

    private void SetupMappedCategory(string code, int id)
    {
        _entityMappingService
            .Setup(service => service.GetMappedNopEntityIdByAkeneoCodeAsync(
                AkeneoEntityType.Category,
                code,
                NopEntityType.Category))
            .ReturnsAsync(id);
        _categoryService
            .Setup(service => service.GetCategoryByIdAsync(id))
            .ReturnsAsync(new Category { Id = id, Name = code });
    }

    private static AkeneoProductSyncContext Context(
        AkeneoCollectionSyncMode mode,
        params string[] categoryCodes)
    {
        var source = AkeneoTestData.Product();
        source.Categories = categoryCodes.ToList();

        return new AkeneoProductSyncContext
        {
            Source = source,
            Product = new Product { Id = 10, Name = "Red Maple" },
            Request = new AkeneoProductImportRequest
            {
                SyncProfileId = 7,
                SyncRunRecordId = 20,
                CategorySyncMode = mode
            },
            Result = new AkeneoProductImportResult()
        };
    }
}
