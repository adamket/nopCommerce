using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services.Planning;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public partial class AkeneoProductCategorySynchronizer
    : IAkeneoProductSectionSynchronizer,
      IAkeneoSectionDryRunPlanProvider
{
    private readonly ICategoryService _categoryService;
    private readonly IAkeneoNopEntityMappingService _entityMappingService;
    private readonly IAkeneoManagedRelationService _managedRelationService;

    public AkeneoProductCategorySynchronizer(
        ICategoryService categoryService,
        IAkeneoNopEntityMappingService entityMappingService,
        IAkeneoManagedRelationService managedRelationService)
    {
        _categoryService = categoryService;
        _entityMappingService = entityMappingService;
        _managedRelationService = managedRelationService;
    }

    public int Order => 300;

    public async Task SynchronizeAsync(
        AkeneoProductSyncContext context,
        CancellationToken cancellationToken = default)
    {
        var plan = await BuildPlanAsync(context, cancellationToken);
        await plan.ExecuteAsync(context, cancellationToken);
    }

    private async Task<AkeneoExecutableSectionPlan> BuildPlanAsync(
        AkeneoProductSyncContext context,
        CancellationToken cancellationToken)
    {
        var plan = new AkeneoExecutableSectionPlan();

        if (context.Request.CategorySyncMode == AkeneoCollectionSyncMode.Disabled)
        {
            plan.CoverageNotes.Add(
                "Category synchronization is disabled by the saved sync profile.");
            return plan;
        }

        var product = context.Product ?? context.ExistingProduct;
        var desiredCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var code in context.Source.Categories ?? new List<string>())
        {
            if (!string.IsNullOrWhiteSpace(code))
                desiredCodes.Add(code.Trim());
        }

        foreach (var mapped in context
                     .GetMappings(NopTargetType.Category)
                     .Where(value => value.HasValue))
        {
            foreach (var code in AkeneoSyncValueHelper.GetRawItems(mapped))
            {
                if (!string.IsNullOrWhiteSpace(code))
                    desiredCodes.Add(code.Trim());
            }
        }

        var desiredCategories = new Dictionary<int, (string Code, Category Category)>();
        var desiredStateComplete = true;

        foreach (var code in desiredCodes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var categoryId = await _entityMappingService
                .GetMappedNopEntityIdByAkeneoCodeAsync(
                    AkeneoEntityType.Category,
                    code,
                    NopEntityType.Category);

            if (!categoryId.HasValue)
            {
                desiredStateComplete = false;
                plan.AddReview(
                    "Categories",
                    code,
                    code,
                    $"No nopCommerce category mapping exists for Akeneo category '{code}'.");
                continue;
            }

            var category = await _categoryService.GetCategoryByIdAsync(categoryId.Value);

            if (category == null || category.Deleted)
            {
                desiredStateComplete = false;
                plan.AddReview(
                    "Categories",
                    code,
                    code,
                    $"Akeneo category '{code}' maps to missing or deleted nopCommerce category ID {categoryId.Value}.");
                continue;
            }

            desiredCategories[category.Id] = (code, category);
        }

        var currentMappings = product == null
            ? new List<ProductCategory>()
            : (await _categoryService.GetProductCategoriesByProductIdAsync(
                product.Id,
                showHidden: true)).ToList();

        var currentByCategoryId = currentMappings
            .GroupBy(mapping => mapping.CategoryId)
            .ToDictionary(group => group.Key, group => group.First());

        foreach (var (categoryId, desired) in desiredCategories)
        {
            if (currentByCategoryId.ContainsKey(categoryId))
            {
                plan.AddOperation(
                    "Categories",
                    desired.Category.Name,
                    desired.Code,
                    "Assigned",
                    "Assigned",
                    AkeneoDryRunOperationType.NoChange);
                continue;
            }

            plan.AddOperation(
                "Categories",
                desired.Category.Name,
                desired.Code,
                "Not assigned",
                "Assigned",
                AkeneoDryRunOperationType.Add,
                executeAsync: async _ =>
                {
                    if (product == null)
                        return false;

                    var productCategory = new ProductCategory
                    {
                        ProductId = product.Id,
                        CategoryId = categoryId,
                        DisplayOrder = 0
                    };

                    await _categoryService.InsertProductCategoryAsync(productCategory);

                    if (context.Request.SyncProfileId.HasValue)
                    {
                        await _managedRelationService.UpsertAsync(
                            context.Request.SyncProfileId.Value,
                            context.Request.SyncRunRecordId,
                            product.Id,
                            AkeneoManagedRelationType.ProductCategory,
                            productCategory.Id,
                            akeneoValueCode: categoryId.ToString());
                    }

                    return true;
                });
        }

        if (context.Request.CategorySyncMode == AkeneoCollectionSyncMode.Merge)
            return plan;

        if (!desiredStateComplete)
        {
            const string warning =
                "Category removal was skipped because the complete desired category state could not be resolved.";
            plan.Warnings.Add(warning);
            plan.CoverageNotes.Add(
                "Category removals are not planned because the complete desired category state could not be resolved. The real synchronizer executes this same plan.");
            return plan;
        }

        if (context.Request.CategorySyncMode == AkeneoCollectionSyncMode.ReplaceAll)
        {
            foreach (var staleMapping in currentMappings.Where(mapping =>
                         !desiredCategories.ContainsKey(mapping.CategoryId)))
            {
                var category = await _categoryService.GetCategoryByIdAsync(staleMapping.CategoryId);

                plan.AddOperation(
                    "Categories",
                    category?.Name ?? $"Category #{staleMapping.CategoryId}",
                    null,
                    "Assigned",
                    "Removed",
                    AkeneoDryRunOperationType.Remove,
                    "Replace-all makes the entire category collection match Akeneo.",
                    executeAsync: async _ =>
                    {
                        await _categoryService.DeleteProductCategoryAsync(staleMapping);
                        return true;
                    });
            }

            return plan;
        }

        if (!context.Request.SyncProfileId.HasValue || product == null)
        {
            const string warning =
                "Replace-managed category synchronization requires a saved sync profile. No categories were removed.";
            plan.Warnings.Add(warning);
            plan.CoverageNotes.Add(
                "Replace-managed category removals require a saved sync profile and cannot be planned without one.");
            return plan;
        }

        var managedRelations = await _managedRelationService.GetByProductAsync(
            context.Request.SyncProfileId.Value,
            product.Id,
            AkeneoManagedRelationType.ProductCategory);

        foreach (var relation in managedRelations)
        {
            var current = currentMappings.FirstOrDefault(mapping =>
                mapping.Id == relation.NopRelationEntityId);

            if (current == null)
            {
                plan.AddInternalOperation(async _ =>
                {
                    await _managedRelationService.DeleteAsync(relation);
                    return false;
                });
                continue;
            }

            if (desiredCategories.ContainsKey(current.CategoryId))
                continue;

            var category = await _categoryService.GetCategoryByIdAsync(current.CategoryId);

            plan.AddOperation(
                "Categories",
                category?.Name ?? $"Category #{current.CategoryId}",
                relation.AkeneoValueCode,
                "Assigned",
                "Removed",
                AkeneoDryRunOperationType.Remove,
                "This relationship is owned by the plugin and is stale for this profile.",
                executeAsync: async _ =>
                {
                    await _categoryService.DeleteProductCategoryAsync(current);
                    await _managedRelationService.DeleteAsync(relation);
                    return true;
                });
        }

        return plan;
    }
}
