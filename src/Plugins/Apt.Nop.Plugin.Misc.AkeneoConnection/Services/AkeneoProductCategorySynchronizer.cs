using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public class AkeneoProductCategorySynchronizer(
    ICategoryService categoryService,
    IAkeneoNopEntityMappingService entityMappingService,
    IAkeneoManagedRelationService managedRelationService)
    : IAkeneoProductSectionSynchronizer
{
    public int Order => 300;

    public async Task SynchronizeAsync(
        AkeneoProductSyncContext context,
        CancellationToken cancellationToken = default)
    {
        if (context.Product == null ||
            context.Request.CategorySyncMode == AkeneoCollectionSyncMode.Disabled)
        {
            return;
        }

        var desiredCategoryIds = new HashSet<int>();
        var desiredStateComplete = true;

        foreach (var categoryCode in context.Source.Categories
                     ?.Where(code => !string.IsNullOrWhiteSpace(code))
                     .Select(code => code.Trim())
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                 ?? Enumerable.Empty<string>())
        {
            desiredStateComplete &= await AddDesiredCategoryAsync(
                categoryCode,
                desiredCategoryIds,
                context);
        }

        foreach (var mapped in context
                     .GetMappings(NopTargetType.Category)
                     .Where(value => value.HasValue))
        {
            foreach (var categoryCode in AkeneoSyncValueHelper.GetRawItems(mapped))
            {
                desiredStateComplete &= await AddDesiredCategoryAsync(
                    categoryCode,
                    desiredCategoryIds,
                    context);
            }
        }

        var currentMappings =
            await categoryService.GetProductCategoriesByProductIdAsync(
                context.Product.Id,
                showHidden: true);

        var currentByCategoryId = currentMappings
            .GroupBy(mapping => mapping.CategoryId)
            .ToDictionary(group => group.Key, group => group.First());

        foreach (var categoryId in desiredCategoryIds)
        {
            if (currentByCategoryId.ContainsKey(categoryId))
                continue;

            var productCategory = new ProductCategory
            {
                ProductId = context.Product.Id,
                CategoryId = categoryId,
                DisplayOrder = 0
            };

            await categoryService.InsertProductCategoryAsync(productCategory);

            if (context.Request.SyncProfileId.HasValue)
            {
                await managedRelationService.UpsertAsync(
                    context.Request.SyncProfileId.Value,
                    context.Request.SyncRunRecordId,
                    context.Product.Id,
                    AkeneoManagedRelationType.ProductCategory,
                    productCategory.Id,
                    akeneoValueCode: categoryId.ToString());
            }

            context.MarkChanged();
        }

        if (context.Request.CategorySyncMode == AkeneoCollectionSyncMode.Merge)
            return;

        if (!desiredStateComplete)
        {
            context.Result.AddWarning(
                "Category removal was skipped because the complete desired category state could not be resolved.");
            return;
        }

        if (context.Request.CategorySyncMode == AkeneoCollectionSyncMode.ReplaceAll)
        {
            foreach (var staleMapping in currentMappings.Where(mapping =>
                         !desiredCategoryIds.Contains(mapping.CategoryId)))
            {
                await categoryService.DeleteProductCategoryAsync(staleMapping);
                context.MarkChanged();
            }

            return;
        }

        if (!context.Request.SyncProfileId.HasValue)
        {
            context.Result.AddWarning(
                "Replace-managed category synchronization requires a sync profile. No categories were removed.");
            return;
        }

        var managedRelations = await managedRelationService.GetByProductAsync(
            context.Request.SyncProfileId.Value,
            context.Product.Id,
            AkeneoManagedRelationType.ProductCategory);

        foreach (var relation in managedRelations)
        {
            var current = currentMappings.FirstOrDefault(mapping =>
                mapping.Id == relation.NopRelationEntityId);

            if (current == null)
            {
                await managedRelationService.DeleteAsync(relation);
                continue;
            }

            if (desiredCategoryIds.Contains(current.CategoryId))
                continue;

            await categoryService.DeleteProductCategoryAsync(current);
            await managedRelationService.DeleteAsync(relation);
            context.MarkChanged();
        }
    }

    private async Task<bool> AddDesiredCategoryAsync(
        string categoryCode,
        ISet<int> desiredCategoryIds,
        AkeneoProductSyncContext context)
    {
        var categoryId = await entityMappingService
            .GetMappedNopEntityIdByAkeneoCodeAsync(
                AkeneoEntityType.Category,
                categoryCode,
                NopEntityType.Category);

        if (!categoryId.HasValue)
        {
            context.Result.AddWarning(
                $"No nopCommerce category mapping exists for Akeneo category '{categoryCode}'.");
            return false;
        }

        var category = await categoryService.GetCategoryByIdAsync(categoryId.Value);

        if (category == null || category.Deleted)
        {
            context.Result.AddWarning(
                $"Akeneo category '{categoryCode}' maps to missing or deleted nopCommerce category ID {categoryId.Value}.");
            return false;
        }

        desiredCategoryIds.Add(category.Id);
        return true;
    }
}
