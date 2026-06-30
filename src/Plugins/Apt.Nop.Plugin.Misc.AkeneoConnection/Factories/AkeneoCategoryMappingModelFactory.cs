using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;

public class AkeneoCategoryMappingModelFactory(
    IAkeneoApiClient akeneoApiClient,
    IAkeneoNopEntityMappingService akeneoNopEntityMappingService,
    ICategoryService categoryService)
    : IAkeneoCategoryMappingModelFactory
{
    public async Task<AkeneoCategoryMappingListModel> PrepareCategoryMappingListModelAsync()
    {
        var model = new AkeneoCategoryMappingListModel();

        var nopCategories = await categoryService.GetAllCategoriesAsync(showHidden: true);

        model.AvailableNopCategories.Add(new SelectListItem
        {
            Text = "-- Select nopCommerce category --",
            Value = "0"
        });

        foreach (var category in nopCategories)
        {
            var breadcrumb = await categoryService.GetFormattedBreadCrumbAsync(category, nopCategories);

            model.AvailableNopCategories.Add(new SelectListItem
            {
                Text = breadcrumb,
                Value = category.Id.ToString()
            });
        }

        var nopCategoryLookup = nopCategories.ToDictionary(
            category => category.Id,
            category => category.Name);

        var mappings = await akeneoNopEntityMappingService.GetAkeneoNopEntityMappingsAsync(
            AkeneoEntityType.Category);

        var categoryMappings = mappings
            .Where(mapping => mapping.NopEntityTypeId == (int)NopEntityType.Category)
            .GroupBy(mapping => mapping.AkeneoCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);

        var akeneoCategories = await akeneoApiClient.GetCategoriesAsync(limit: 100);

        var categoryRows = akeneoCategories
            .Select(ParseAkeneoCategory)
            .Where(category => !string.IsNullOrWhiteSpace(category.AkeneoCode))
            .ToList();

        var orderedRows = OrderByHierarchy(categoryRows);

        foreach (var row in orderedRows)
        {
            if (categoryMappings.TryGetValue(row.AkeneoCode, out var existingMapping))
            {
                row.NopCategoryId = existingMapping.NopEntityId;

                if (nopCategoryLookup.TryGetValue(existingMapping.NopEntityId, out var nopCategoryName))
                    row.NopCategoryName = nopCategoryName;
            }

            model.Rows.Add(row);
        }

        return model;
    }

    private static AkeneoCategoryMappingModel ParseAkeneoCategory(
        AkeneoCategoryDefinition category)
    {
        var code = category.Code?.Trim();
        var parent = category.Parent?.Trim();
        var label = category.GetLabel()?.Trim();

        return new AkeneoCategoryMappingModel
        {
            AkeneoCode = code,
            AkeneoParentCode = parent,
            AkeneoLabel = string.IsNullOrWhiteSpace(label) ? code : label
        };
    }

    private static IList<AkeneoCategoryMappingModel> OrderByHierarchy(
        IList<AkeneoCategoryMappingModel> categories)
    {
        var result = new List<AkeneoCategoryMappingModel>();

        var byCode = categories.ToDictionary(
            category => category.AkeneoCode,
            category => category,
            StringComparer.OrdinalIgnoreCase);

        var childrenByParent = categories
            .Where(category => !string.IsNullOrWhiteSpace(category.AkeneoParentCode))
            .GroupBy(category => category.AkeneoParentCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(category => category.AkeneoLabel)
                    .ThenBy(category => category.AkeneoCode)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);

        var roots = categories
            .Where(category =>
                string.IsNullOrWhiteSpace(category.AkeneoParentCode) ||
                !byCode.ContainsKey(category.AkeneoParentCode))
            .OrderBy(category => category.AkeneoLabel)
            .ThenBy(category => category.AkeneoCode)
            .ToList();

        foreach (var root in roots)
            addCategoryAndChildren(root, 0);

        return result;

        void addCategoryAndChildren(AkeneoCategoryMappingModel category, int level)
        {
            category.Level = level;
            result.Add(category);

            if (!childrenByParent.TryGetValue(category.AkeneoCode, out var children))
                return;

            foreach (var child in children)
                addCategoryAndChildren(child, level + 1);
        }
    }
}
