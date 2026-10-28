using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Categories;
using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Services.Catalog;
using Nop.Services.Localization;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;

public class AkeneoCategoryMappingModelFactory(
    IAkeneoApiClient akeneoApiClient,
    IAkeneoNopEntityMappingService akeneoNopEntityMappingService,
    ICategoryService categoryService,
    IAkeneoCategoryContentMappingService categoryContentMappingService,
    ILanguageService languageService)
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

        IReadOnlyList<AkeneoCategoryDefinition> akeneoCategories;
        try
        {
            akeneoCategories = await akeneoApiClient.GetCategoriesWithValuesAsync(limit: 100);
        }
        catch (Exception ex)
        {
            // Category mapping still works without template values; only content mapping needs them.
            model.ContentReadError = $"Akeneo category template values could not be read: {ex.Message}";
            akeneoCategories = await akeneoApiClient.GetCategoriesAsync(limit: 100);
        }

        var categoryRows = akeneoCategories
            .Select(ParseAkeneoCategory)
            .Where(category => !string.IsNullOrWhiteSpace(category.AkeneoCode))
            .ToList();

        var orderedRows = OrderByHierarchy(categoryRows);

        var contentStates = (await categoryContentMappingService.GetAllStatesAsync())
            .GroupBy(state => state.AkeneoCategoryCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var row in orderedRows)
        {
            if (categoryMappings.TryGetValue(row.AkeneoCode, out var existingMapping))
            {
                row.NopCategoryId = existingMapping.NopEntityId;

                if (nopCategoryLookup.TryGetValue(existingMapping.NopEntityId, out var nopCategoryName))
                    row.NopCategoryName = nopCategoryName;
            }

            if (contentStates.TryGetValue(row.AkeneoCode, out var state))
            {
                row.ContentStatusId = state.LastStatusId;
                row.ContentMessage = state.LastMessage;
                row.ContentSyncedOnUtc = state.LastSyncedOnUtc;
            }

            model.Rows.Add(row);
        }

        model.TemplateAttributes = DescribeTemplateAttributes(akeneoCategories);
        model.LabelLocales = akeneoCategories
            .SelectMany(category => category.Labels?.Keys ?? Enumerable.Empty<string>())
            .Where(locale => !string.IsNullOrWhiteSpace(locale))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(locale => locale, StringComparer.OrdinalIgnoreCase)
            .ToList();

        model.ContentMappings = (await categoryContentMappingService.GetAllMappingsAsync())
            .Select(mapping => new AkeneoCategoryContentMappingModel
            {
                Id = mapping.Id,
                TargetId = mapping.TargetId,
                LanguageId = mapping.LanguageId,
                SourceTypeId = mapping.SourceTypeId,
                AkeneoAttributeCode = mapping.AkeneoAttributeCode,
                Locale = mapping.Locale,
                Channel = mapping.Channel,
                EmptyValueHandlingId = mapping.EmptyValueHandlingId
            })
            .ToList();

        var languages = await languageService.GetAllLanguagesAsync(showHidden: true);
        model.AvailableLanguages = languages
            .Select(language => new SelectListItem
            {
                Value = language.Id.ToString(),
                Text = language.Published ? language.Name : $"{language.Name} (unpublished)"
            })
            .ToList();
        model.LanguageCultures = languages.ToDictionary(language => language.Id, language => language.LanguageCulture ?? string.Empty);
        model.TranslationLocales = await categoryContentMappingService.GetTranslationLocalesAsync();

        return model;
    }

    /// <summary>
    /// Every template attribute used on at least one category, with the
    /// locale/channel combinations its values are stored under.
    /// </summary>
    public static IList<AkeneoCategoryTemplateAttributeModel> DescribeTemplateAttributes(
        IEnumerable<AkeneoCategoryDefinition> categories)
    {
        var values = categories
            .SelectMany(category => AkeneoCategoryValueReader.Read(category.Values)
                .Select(value => (Category: category.Code, Value: value)));

        return values
            .GroupBy(item => item.Value.AttributeCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => new AkeneoCategoryTemplateAttributeModel
            {
                Code = group.First().Value.AttributeCode,
                Type = group
                    .Select(item => item.Value.Type)
                    .FirstOrDefault(type => !string.IsNullOrWhiteSpace(type)),
                IsImage = group.Any(item => item.Value.IsImage),
                CategoryCount = group
                    .Select(item => item.Category)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count(),
                Scopes = group
                    .Select(item => (item.Value.Locale, item.Value.Channel))
                    .Distinct()
                    .OrderBy(scope => scope.Channel ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(scope => scope.Locale ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .Select(scope => new AkeneoCategoryValueScopeModel { Locale = scope.Locale, Channel = scope.Channel })
                    .ToList()
            })
            .OrderBy(attribute => attribute.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
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
