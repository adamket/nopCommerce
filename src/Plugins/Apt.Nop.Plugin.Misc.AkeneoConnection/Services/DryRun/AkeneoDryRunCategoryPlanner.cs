using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;
using static Apt.Nop.Plugin.Misc.AkeneoConnection.Services.DryRun.AkeneoDryRunPlanHelper;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services.DryRun;

public sealed class AkeneoDryRunCategoryPlanner(
    ICategoryService categoryService,
    IAkeneoNopEntityMappingService entityMappingService,
    IAkeneoManagedRelationService managedRelationService)
    : IAkeneoDryRunSectionPlanner
{
    public int Order => 300;

    public async Task PlanAsync(
        AkeneoProductSyncContext context,
        AkeneoProductMappingPreviewModel model,
        CancellationToken cancellationToken = default)
    {
        if (context.Request.CategorySyncMode == AkeneoCollectionSyncMode.Disabled)
        {
            model.CoverageNotes.Add(
                "Category synchronization is disabled by the saved sync profile.");
            return;
        }

        var desiredCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var code in context.Source.Categories ?? new List<string>())
        {
            if (!string.IsNullOrWhiteSpace(code))
                desiredCodes.Add(code.Trim());
        }

        foreach (var mapped in context.GetMappings(NopTargetType.Category).Where(value => value.HasValue))
        {
            foreach (var code in AkeneoSyncValueHelper.GetRawItems(mapped))
            {
                if (!string.IsNullOrWhiteSpace(code))
                    desiredCodes.Add(code.Trim());
            }
        }

        var desiredCategories = new Dictionary<int, (string Code, string Name)>();
        var desiredStateComplete = true;

        foreach (var code in desiredCodes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var categoryId = await entityMappingService
                .GetMappedNopEntityIdByAkeneoCodeAsync(
                    AkeneoEntityType.Category,
                    code,
                    NopEntityType.Category);

            if (!categoryId.HasValue)
            {
                desiredStateComplete = false;
                AddReviewOperation(
                    model,
                    "Categories",
                    code,
                    code,
                    "No nopCommerce category mapping exists for this Akeneo category.");
                continue;
            }

            var category = await categoryService.GetCategoryByIdAsync(categoryId.Value);
            if (category == null || category.Deleted)
            {
                desiredStateComplete = false;
                AddReviewOperation(
                    model,
                    "Categories",
                    code,
                    code,
                    $"The category maps to missing or deleted nopCommerce category ID {categoryId.Value}.");
                continue;
            }

            desiredCategories[category.Id] = (code, category.Name);
        }

        var product = context.ExistingProduct;
        var currentMappings = product == null
            ? new List<ProductCategory>()
            : (await categoryService.GetProductCategoriesByProductIdAsync(
                product.Id,
                showHidden: true)).ToList();

        var currentByCategoryId = currentMappings
            .GroupBy(mapping => mapping.CategoryId)
            .ToDictionary(group => group.Key, group => group.First());

        foreach (var (categoryId, desired) in desiredCategories)
        {
            var exists = currentByCategoryId.ContainsKey(categoryId);
            AddOperation(
                model,
                "Categories",
                desired.Name,
                desired.Code,
                exists ? "Assigned" : "Not assigned",
                "Assigned",
                exists
                    ? AkeneoDryRunOperationType.NoChange
                    : AkeneoDryRunOperationType.Add);
        }

        if (context.Request.CategorySyncMode == AkeneoCollectionSyncMode.Merge)
            return;

        if (!desiredStateComplete)
        {
            model.CoverageNotes.Add(
                "Category removals are not planned because the complete desired category state could not be resolved. The real synchronizer uses the same safety rule.");
            return;
        }

        IEnumerable<ProductCategory> staleMappings;

        if (context.Request.CategorySyncMode == AkeneoCollectionSyncMode.ReplaceAll)
        {
            staleMappings = currentMappings.Where(mapping =>
                !desiredCategories.ContainsKey(mapping.CategoryId));
        }
        else if (context.Request.SyncProfileId.HasValue && product != null)
        {
            var managed = await managedRelationService.GetByProductAsync(
                context.Request.SyncProfileId.Value,
                product.Id,
                AkeneoManagedRelationType.ProductCategory);

            var managedIds = managed.Select(relation => relation.NopRelationEntityId).ToHashSet();
            staleMappings = currentMappings.Where(mapping =>
                managedIds.Contains(mapping.Id) &&
                !desiredCategories.ContainsKey(mapping.CategoryId));
        }
        else
        {
            model.CoverageNotes.Add(
                "Replace-managed category removals require a saved sync profile and cannot be planned without one.");
            return;
        }

        foreach (var mapping in staleMappings)
        {
            var category = await categoryService.GetCategoryByIdAsync(mapping.CategoryId);
            AddOperation(
                model,
                "Categories",
                category?.Name ?? $"Category #{mapping.CategoryId}",
                null,
                "Assigned",
                "Removed",
                AkeneoDryRunOperationType.Remove,
                context.Request.CategorySyncMode == AkeneoCollectionSyncMode.ReplaceManaged
                    ? "This relationship is owned by the plugin and is stale for this profile."
                    : "Replace-all makes the entire category collection match Akeneo.");
        }
    }
}
