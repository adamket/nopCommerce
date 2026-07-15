using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;

public class AkeneoProductCategorySynchronizer(
    ICategoryService categoryService,
    IAkeneoNopEntityMappingService entityMappingService)
    : IAkeneoProductSectionSynchronizer
{
    public int Order => 300;

    public async Task SynchronizeAsync(
        AkeneoProductSyncContext context,
        CancellationToken cancellationToken = default)
    {
        if (context.Product == null ||
            context.Request.CategorySyncMode ==
                AkeneoCollectionSyncMode.Disabled)
        {
            return;
        }

        var desiredCategoryIds = new HashSet<int>();

        foreach (var categoryCode in context.Source.Categories
                     ?.Where(code => !string.IsNullOrWhiteSpace(code))
                     .Select(code => code.Trim())
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                 ?? Enumerable.Empty<string>())
        {
            await AddDesiredCategoryAsync(
                categoryCode,
                desiredCategoryIds,
                context);
        }

        foreach (var mapped in context
                     .GetMappings(NopTargetType.Category)
                     .Where(value => value.HasValue))
        {
            foreach (var categoryCode in
                     AkeneoSyncValueHelper.GetRawItems(mapped))
            {
                await AddDesiredCategoryAsync(
                    categoryCode,
                    desiredCategoryIds,
                    context);
            }
        }

        var currentMappings =
            await categoryService.GetProductCategoriesByProductIdAsync(
                context.Product.Id,
                showHidden: true);

        var currentCategoryIds = currentMappings
            .Select(mapping => mapping.CategoryId)
            .ToHashSet();

        foreach (var categoryId in desiredCategoryIds)
        {
            if (currentCategoryIds.Contains(categoryId))
                continue;

            await categoryService.InsertProductCategoryAsync(
                new ProductCategory
                {
                    ProductId = context.Product.Id,
                    CategoryId = categoryId,
                    DisplayOrder = 0
                });

            context.MarkChanged();
        }

        if (context.Request.CategorySyncMode !=
            AkeneoCollectionSyncMode.Replace)
        {
            return;
        }

        foreach (var staleMapping in currentMappings.Where(mapping =>
                     !desiredCategoryIds.Contains(mapping.CategoryId)))
        {
            await categoryService.DeleteProductCategoryAsync(
                staleMapping);

            context.MarkChanged();
        }
    }

    private async Task AddDesiredCategoryAsync(
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
                $"No nopCommerce category mapping exists for " +
                $"Akeneo category '{categoryCode}'.");

            return;
        }

        desiredCategoryIds.Add(categoryId.Value);
    }
}