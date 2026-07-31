using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Extensions;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models.SyncProfiles;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Services;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;

public class AkeneoSyncProfileModelFactory(
    IAkeneoSyncProfileService syncProfileService,
    IAkeneoApiClient akeneoApiClient)
    : IAkeneoSyncProfileModelFactory
{
    public async Task<AkeneoSyncProfileListModel> PrepareListModelAsync()
    {
        var profiles = await syncProfileService.GetAllAkeneoSyncProfilesAsync();

        var model = new AkeneoSyncProfileListModel();

        foreach (var profile in profiles)
            model.Profiles.Add(await PrepareModelAsync(profile));

        return model;
    }

    public async Task<AkeneoSyncProfileModel> PrepareModelAsync(
        AkeneoSyncProfile profile = null)
    {
        var model = new AkeneoSyncProfileModel
        {
            Enabled = true,
            AkeneoChannel = "ecommerce",
            AkeneoLocales = "en_US",
            SelectedAkeneoLocaleCodes = new List<string> { "en_US" },

            ProductWriteModeId = (int)AkeneoProductWriteMode .CreateAndUpdate,
            UnmappedAttributeBehaviorId = (int)UnmappedAkeneoAttributeBehavior.Ignore,

            PageSize = 100,
            ContinueOnError = true,
            SaveRawPayloadSnapshot = false,
         //   AddMappedManufacturers = false,
            CreateMissingSpecificationAttributeOptions = true,
            CreateMissingProductAttributeValues = true,

            CategoryFilterModeId = (int)AkeneoCategoryFilterMode.None,
            ProductEnabledFilterId = (int)AkeneoProductEnabledFilter.Any,
            ProductParentFilterModeId = (int)AkeneoProductParentFilterMode.Any,
            CurrencyCode = "USD",

            ProductFieldMissingValueBehaviorId =
                (int)AkeneoMissingValueBehavior.PreserveExisting,

            SeoFieldMissingValueBehaviorId =
                (int)AkeneoMissingValueBehavior.PreserveExisting,

            CustomPropertyMissingValueBehaviorId =
                (int)AkeneoMissingValueBehavior.PreserveExisting,

            CategorySyncModeId =
                (int)AkeneoCollectionSyncMode.Disabled,

            SpecificationAttributeSyncModeId =
                (int)AkeneoCollectionSyncMode.Merge,

            ProductAttributeSyncModeId =
                (int)AkeneoCollectionSyncMode.Merge,

            AssetSyncModeId =
                (int)AkeneoCollectionSyncMode.ReplaceManaged,

            MissingProductBehaviorId =
                (int)AkeneoMissingProductBehavior.Ignore,

            UpdatedFilterModeId =
                (int)AkeneoUpdatedFilterMode.SinceLastSuccessfulRun,

            IncludeLinkedAssetUpdates = false,
        };

        if (profile != null)
        {
            model.Id = profile.Id;
            model.Name = profile.Name;
            model.Enabled = profile.Enabled;
            model.AkeneoChannel = profile.AkeneoChannel;
            model.AkeneoLocales = profile.AkeneoLocales.SplitCsv().FirstOrDefault() ?? "en-US";
            model.AkeneoFamilyCodes = profile.AkeneoFamilyCodes;
            model.AkeneoCategoryCodes = profile.AkeneoCategoryCodes;
            model.AkeneoProductGroupCodes = profile.AkeneoProductGroupCodes;

            model.ProductWriteModeId = profile.ProductWriteModeId;
            model.UnmappedAttributeBehaviorId = profile.UnmappedAttributeBehaviorId;

            model.PageSize = profile.PageSize;
            model.MaxProducts = profile.MaxProducts;
            model.ContinueOnError = profile.ContinueOnError;
            model.SaveRawPayloadSnapshot = profile.SaveRawPayloadSnapshot;

            model.CategorySyncModeId = profile.CategorySyncModeId;
          //  model.AddMappedManufacturers = profile.AddMappedManufacturers;
            model.CreateMissingSpecificationAttributeOptions = profile.CreateMissingSpecificationAttributeOptions;
            model.CreateMissingProductAttributeValues = profile.CreateMissingProductAttributeValues;
          
            model.CategoryFilterModeId = profile.CategoryFilterModeId;
            model.ProductEnabledFilterId = profile.ProductEnabledFilterId;
            model.UpdatedAfterUtc = profile.UpdatedAfterUtc;
            model.UpdatedSinceLastNDays = profile.UpdatedSinceLastNDays;
            model.ProductParentFilterModeId = profile.ProductParentFilterModeId;
            model.AdditionalSearchJson = profile.AdditionalSearchJson;

            model.SelectedAkeneoProductGroupCodes = profile.AkeneoProductGroupCodes.SplitCsv();
            model.SelectedAkeneoLocaleCodes = profile.AkeneoLocales.SplitCsv();
            model.SelectedAkeneoFamilyCodes = profile.AkeneoFamilyCodes.SplitCsv();
            model.SelectedAkeneoCategoryCodes = profile.AkeneoCategoryCodes.SplitCsv();

            model.CurrencyCode = profile.CurrencyCode;

            model.ProductFieldMissingValueBehaviorId =
                profile.ProductFieldMissingValueBehaviorId;

            model.SeoFieldMissingValueBehaviorId =
                profile.SeoFieldMissingValueBehaviorId;

            model.CustomPropertyMissingValueBehaviorId =
                profile.CustomPropertyMissingValueBehaviorId;

            model.CategorySyncModeId =
                profile.CategorySyncModeId;

            model.SpecificationAttributeSyncModeId =
                profile.SpecificationAttributeSyncModeId;

            model.ProductAttributeSyncModeId =
                profile.ProductAttributeSyncModeId;

            model.AssetSyncModeId =
                profile.AssetSyncModeId;

            model.MissingProductBehaviorId =
                profile.MissingProductBehaviorId;

            model.UpdatedFilterModeId =
                profile.UpdatedFilterModeId;

            model.IncludeLinkedAssetUpdates =
                profile.IncludeLinkedAssetUpdates;

        }

        PrepareDisplayNames(model);
        PrepareStaticDropdowns(model);
        await PrepareAvailableOptionsAsync(model);

        return model;
    }

    public async Task PrepareAvailableOptionsAsync(
        AkeneoSyncProfileModel model)
    {
        PrepareStaticDropdowns(model);

        await PrepareAkeneoChannelOptionsAsync(model);
        await PrepareAkeneoLocaleOptionsAsync(model);
        await PrepareAkeneoFamilyOptionsAsync(model);
        await PrepareAkeneoCategoryOptionsAsync(model);

        PrepareAkeneoProductGroupCodes(model);
        PrepareDisplayNames(model);
    }

    private async Task PrepareAkeneoFamilyOptionsAsync(
    AkeneoSyncProfileModel model)
    {
        var selectedFamilyCodes = model.SelectedAkeneoFamilyCodes?.Any() == true
            ? model.SelectedAkeneoFamilyCodes
            : model.AkeneoFamilyCodes.SplitCsv();

        selectedFamilyCodes = selectedFamilyCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        model.SelectedAkeneoFamilyCodes = selectedFamilyCodes;

        var labelLocale = "en-US"; //ResolvePreferredLabelLocale(model); //TODO Use current store locale?
        var options = new List<SelectListItem>();

        try
        {
            var families = await akeneoApiClient.GetFamiliesAsync();

            foreach (var family in families
                         .Where(family => !string.IsNullOrWhiteSpace(family.Code))
                         .OrderBy(family => family.GetDisplayName(labelLocale), StringComparer.OrdinalIgnoreCase))
            {
                var code = family.Code.Trim();

                options.Add(new SelectListItem
                {
                    Text = family.GetDisplayName(labelLocale),
                    Value = code,
                    Selected = selectedFamilyCodes.Contains(code, StringComparer.OrdinalIgnoreCase)
                });
            }
        }
        catch
        {
            // If Akeneo is unavailable, preserve selected/saved values below.
        }

        foreach (var selectedFamilyCode in selectedFamilyCodes)
        {
            if (options.Any(option => string.Equals(option.Value, selectedFamilyCode, StringComparison.OrdinalIgnoreCase)))
                continue;

            options.Add(new SelectListItem
            {
                Text = selectedFamilyCode,
                Value = selectedFamilyCode,
                Selected = true
            });
        }

        model.AvailableAkeneoFamilies = options
            .OrderBy(option => option.Text, StringComparer.OrdinalIgnoreCase)
            .ToList();

        model.AvailableProductFieldMissingValueBehaviors =
            BuildSelectList(
                Enum.GetValues<AkeneoMissingValueBehavior>(),
                model.ProductFieldMissingValueBehaviorId,
                GetMissingValueBehaviorText);

        model.AvailableSeoFieldMissingValueBehaviors =
            BuildSelectList(
                Enum.GetValues<AkeneoMissingValueBehavior>(),
                model.SeoFieldMissingValueBehaviorId,
                GetMissingValueBehaviorText);

        model.AvailableCustomPropertyMissingValueBehaviors =
            BuildSelectList(
                Enum.GetValues<AkeneoMissingValueBehavior>(),
                model.CustomPropertyMissingValueBehaviorId,
                GetMissingValueBehaviorText);

        model.AvailableCategorySyncModes =
            BuildSelectList(
                Enum.GetValues<AkeneoCollectionSyncMode>(),
                model.CategorySyncModeId,
                GetCollectionSyncModeText);

        model.AvailableSpecificationAttributeSyncModes =
            BuildSelectList(
                Enum.GetValues<AkeneoCollectionSyncMode>(),
                model.SpecificationAttributeSyncModeId,
                GetCollectionSyncModeText);

        model.AvailableProductAttributeSyncModes =
            BuildSelectList(
                Enum.GetValues<AkeneoCollectionSyncMode>(),
                model.ProductAttributeSyncModeId,
                GetCollectionSyncModeText);

        model.AvailableAssetSyncModes =
            BuildSelectList(
                Enum.GetValues<AkeneoCollectionSyncMode>(),
                model.AssetSyncModeId,
                GetCollectionSyncModeText);

        model.AvailableMissingProductBehaviors =
            BuildSelectList(
                Enum.GetValues<AkeneoMissingProductBehavior>(),
                model.MissingProductBehaviorId,
                GetMissingProductBehaviorText);

        model.AvailableUpdatedFilterModes =
            BuildSelectList(
                Enum.GetValues<AkeneoUpdatedFilterMode>(),
                model.UpdatedFilterModeId,
                GetUpdatedFilterModeText);

    }


    private async Task PrepareAkeneoCategoryOptionsAsync(
    AkeneoSyncProfileModel model)
    {
        var selectedCategoryCodes = model.SelectedAkeneoCategoryCodes?.Any() == true
            ? model.SelectedAkeneoCategoryCodes
            : model.AkeneoCategoryCodes.SplitCsv();

        selectedCategoryCodes = selectedCategoryCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        model.SelectedAkeneoCategoryCodes = selectedCategoryCodes;

        var labelLocale = "en-US"; //ResolvePreferredLabelLocale(model); //TODO Use current store locale?
        var options = new List<SelectListItem>();

        try
        {
            var categories = await akeneoApiClient.GetCategoriesAsync();

            foreach (var category in categories
                         .Where(category => !string.IsNullOrWhiteSpace(category.Code))
                         .OrderBy(category => category.GetDisplayName(labelLocale), StringComparer.OrdinalIgnoreCase)
                         .ThenBy(category => category.Code, StringComparer.OrdinalIgnoreCase))
            {
                var code = category.Code.Trim();

                options.Add(new SelectListItem
                {
                    Text = category.GetDisplayNameWithParent(labelLocale),
                    Value = code,
                    Selected = selectedCategoryCodes.Contains(code, StringComparer.OrdinalIgnoreCase)
                });
            }
        }
        catch
        {
            // If Akeneo is unavailable, preserve selected/saved values below.
        }

        foreach (var selectedCategoryCode in selectedCategoryCodes)
        {
            if (options.Any(option => string.Equals(option.Value, selectedCategoryCode, StringComparison.OrdinalIgnoreCase)))
                continue;

            options.Add(new SelectListItem
            {
                Text = selectedCategoryCode,
                Value = selectedCategoryCode,
                Selected = true
            });
        }

        model.AvailableAkeneoCategories = options
            .OrderBy(option => option.Text, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
    private static void PrepareAkeneoProductGroupCodes(
        AkeneoSyncProfileModel model)
    {
        var selectedGroupCodes = model.SelectedAkeneoProductGroupCodes?.Any() == true
            ? model.SelectedAkeneoProductGroupCodes
            : model.AkeneoProductGroupCodes.SplitCsv();

        selectedGroupCodes = selectedGroupCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
            .ToList();

        model.SelectedAkeneoProductGroupCodes = selectedGroupCodes;

        // Optional, only for compatibility if anything still reads this property.
        model.AvailableAkeneoProductGroups = selectedGroupCodes
            .Select(code => new SelectListItem
            {
                Text = code,
                Value = code,
                Selected = true
            })
            .ToList();
    }

    private async Task PrepareAkeneoChannelOptionsAsync(
        AkeneoSyncProfileModel model)
    {
        var selectedChannel = model.AkeneoChannel?.Trim();

        var options = new List<SelectListItem>
        {
            new()
            {
                Text = "Select channel",
                Value = string.Empty,
                Selected = string.IsNullOrWhiteSpace(selectedChannel)
            }
        };

        try
        {
            var channels = await akeneoApiClient.GetChannelsAsync();

            foreach (var channel in channels.OrderBy(channel => channel.Code))
            {
                if (string.IsNullOrWhiteSpace(channel.Code))
                    continue;

                options.Add(new SelectListItem
                {
                    Text = channel.Code,
                    Value = channel.Code,
                    Selected = string.Equals(channel.Code, selectedChannel, StringComparison.OrdinalIgnoreCase)
                });
            }
        }
        catch
        {
            // If Akeneo is unavailable, keep the saved value selectable
            // so the profile can still be edited.
        }

        if (!string.IsNullOrWhiteSpace(selectedChannel) &&
            options.All(option => !string.Equals(option.Value, selectedChannel, StringComparison.OrdinalIgnoreCase)))
        {
            options.Add(new SelectListItem
            {
                Text = selectedChannel,
                Value = selectedChannel,
                Selected = true
            });
        }

        model.AvailableAkeneoChannels = options;
    }

    private async Task PrepareAkeneoLocaleOptionsAsync(
        AkeneoSyncProfileModel model)
    {
        var selectedLocales = model.SelectedAkeneoLocaleCodes?.Any() == true
            ? model.SelectedAkeneoLocaleCodes
            : model.AkeneoLocales.SplitCsv();

        selectedLocales = selectedLocales
            .Where(locale => !string.IsNullOrWhiteSpace(locale))
            .Select(locale => locale.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        model.SelectedAkeneoLocaleCodes = selectedLocales;

        var options = new List<SelectListItem>();

        try
        {
            var locales = await akeneoApiClient.GetLocalesAsync();

            foreach (var locale in locales)
            {
                var code = locale.GetRootString("code");

                if (string.IsNullOrWhiteSpace(code))
                    continue;

                options.Add(new SelectListItem
                {
                    Text = code,
                    Value = code,
                    Selected = selectedLocales.Contains(code, StringComparer.OrdinalIgnoreCase)
                });
            }
        }
        catch
        {
            // If Akeneo is unavailable, fallback to selected/saved locales below.
        }

        foreach (var selectedLocale in selectedLocales)
        {
            if (options.Any(option => string.Equals(option.Value, selectedLocale, StringComparison.OrdinalIgnoreCase)))
                continue;

            options.Add(new SelectListItem
            {
                Text = selectedLocale,
                Value = selectedLocale,
                Selected = true
            });
        }

        model.AvailableAkeneoLocales = options
            .OrderBy(option => option.Value)
            .ToList();
    }

    private static void PrepareStaticDropdowns(
        AkeneoSyncProfileModel model)
    {
        model.AvailableProductWriteModes = BuildSelectList(
            Enum.GetValues<AkeneoProductWriteMode>(),
            model.ProductWriteModeId,
            GetProductWriteModeText);

        model.AvailableUnmappedAttributeBehaviors = BuildSelectList(
            Enum.GetValues<UnmappedAkeneoAttributeBehavior>(),
            model.UnmappedAttributeBehaviorId,
            GetUnmappedAttributeBehaviorText);

        model.AvailableCategoryFilterModes = BuildSelectList(
            Enum.GetValues<AkeneoCategoryFilterMode>(),
            model.CategoryFilterModeId,
            GetCategoryFilterModeText);

        model.AvailableProductEnabledFilters = BuildSelectList(
            Enum.GetValues<AkeneoProductEnabledFilter>(),
            model.ProductEnabledFilterId,
            GetProductEnabledFilterText);

        model.AvailableProductParentFilterModes = BuildSelectList(
            Enum.GetValues<AkeneoProductParentFilterMode>(),
            model.ProductParentFilterModeId,
            GetProductParentFilterModeText);
    }

    private static void PrepareDisplayNames(
        AkeneoSyncProfileModel model)
    {
        model.ProductWriteModeName = GetProductWriteModeText(
            Enum.IsDefined(typeof(AkeneoProductWriteMode), model.ProductWriteModeId)
                ? (AkeneoProductWriteMode)model.ProductWriteModeId
                : AkeneoProductWriteMode.CreateAndUpdate);

        model.UnmappedAttributeBehaviorName = GetUnmappedAttributeBehaviorText(
            Enum.IsDefined(typeof(UnmappedAkeneoAttributeBehavior), model.UnmappedAttributeBehaviorId)
                ? (UnmappedAkeneoAttributeBehavior)model.UnmappedAttributeBehaviorId
                : UnmappedAkeneoAttributeBehavior.Ignore);

        model.CategoryFilterModeName = GetCategoryFilterModeText(
            Enum.IsDefined(typeof(AkeneoCategoryFilterMode), model.CategoryFilterModeId)
                ? (AkeneoCategoryFilterMode)model.CategoryFilterModeId
                : AkeneoCategoryFilterMode.None);

        model.ProductEnabledFilterName = GetProductEnabledFilterText(
            Enum.IsDefined(typeof(AkeneoProductEnabledFilter), model.ProductEnabledFilterId)
                ? (AkeneoProductEnabledFilter)model.ProductEnabledFilterId
                : AkeneoProductEnabledFilter.Any);

        model.ProductParentFilterModeName = GetProductParentFilterModeText(
            Enum.IsDefined(typeof(AkeneoProductParentFilterMode), model.ProductParentFilterModeId)
                ? (AkeneoProductParentFilterMode)model.ProductParentFilterModeId
                : AkeneoProductParentFilterMode.Any);
    }

    private static IList<SelectListItem> BuildSelectList<TEnum>(
        IEnumerable<TEnum> values,
        int selectedValue,
        Func<TEnum, string> textFactory)
        where TEnum : Enum
    {
        return values
            .Select(value =>
            {
                var intValue = Convert.ToInt32(value);

                return new SelectListItem
                {
                    Text = textFactory(value),
                    Value = intValue.ToString(),
                    Selected = selectedValue == intValue
                };
            })
            .ToList();
    }



    //private static string GetRootString(
    //    JsonElement element,
    //    string propertyName)
    //{
    //    if (!element.TryGetProperty(propertyName, out var property))
    //        return null;

    //    return property.ValueKind == JsonValueKind.String
    //        ? property.GetString()
    //        : null;
    //}

    private static string GetProductWriteModeText(AkeneoProductWriteMode mode)
    {
        return mode switch
        {
            AkeneoProductWriteMode.CreateAndUpdate => "Create and update",
            AkeneoProductWriteMode.CreateOnly => "Create only",
            AkeneoProductWriteMode.UpdateOnly => "Update only",
            _ => mode.ToString()
        };
    }

    private static string GetUnmappedAttributeBehaviorText(UnmappedAkeneoAttributeBehavior behavior)
    {
        return behavior switch
        {
            UnmappedAkeneoAttributeBehavior.Ignore => "Ignore",
            UnmappedAkeneoAttributeBehavior.Log => "Log only",
            UnmappedAkeneoAttributeBehavior.ImportAsCustomProperty => "Import as custom property",
            UnmappedAkeneoAttributeBehavior.ImportAsSpecificationAttribute => "Import as specification attribute",
            _ => behavior.ToString()
        };
    }

    private static string GetCategoryFilterModeText(AkeneoCategoryFilterMode mode)
    {
        return mode switch
        {
            AkeneoCategoryFilterMode.None => "No category filter",
            AkeneoCategoryFilterMode.In => "In selected categories",
            AkeneoCategoryFilterMode.InChildren => "In selected categories or children",
            AkeneoCategoryFilterMode.NotIn => "Not in selected categories",
            AkeneoCategoryFilterMode.NotInChildren => "Not in selected categories or children",
            AkeneoCategoryFilterMode.Unclassified => "Unclassified only",
            AkeneoCategoryFilterMode.InOrUnclassified => "In selected categories or unclassified",
            _ => mode.ToString()
        };
    }

    private static string GetProductEnabledFilterText(AkeneoProductEnabledFilter filter)
    {
        return filter switch
        {
            AkeneoProductEnabledFilter.Any => "Any",
            AkeneoProductEnabledFilter.EnabledOnly => "Enabled only",
            AkeneoProductEnabledFilter.DisabledOnly => "Disabled only",
            _ => filter.ToString()
        };
    }

    private static string GetProductParentFilterModeText(AkeneoProductParentFilterMode mode)
    {
        return mode switch
        {
            AkeneoProductParentFilterMode.Any => "Any",
            AkeneoProductParentFilterMode.SimpleProductsOnly => "Simple products only",
            AkeneoProductParentFilterMode.VariantProductsOnly => "Variant products only",
            _ => mode.ToString()
        };
    }
    private static string GetMissingValueBehaviorText(
        AkeneoMissingValueBehavior behavior)
    {
        return behavior switch
        {
            AkeneoMissingValueBehavior.PreserveExisting =>
                "Preserve existing nopCommerce value",

            AkeneoMissingValueBehavior.ClearExisting =>
                "Clear existing nopCommerce value",

            _ => behavior.ToString()
        };
    }

    private static string GetCollectionSyncModeText(
        AkeneoCollectionSyncMode mode)
    {
        return mode switch
        {
            AkeneoCollectionSyncMode.Disabled =>
                "Disabled",

            AkeneoCollectionSyncMode.Merge =>
                "Merge with existing nopCommerce values",

            AkeneoCollectionSyncMode.ReplaceManaged =>
                "Replace plugin-managed values",

            AkeneoCollectionSyncMode.ReplaceAll =>
                "Replace all values (destructive)",

            _ => mode.ToString()
        };
    }


    private static string GetMissingProductBehaviorText(
        AkeneoMissingProductBehavior behavior)
    {
        return behavior switch
        {
            AkeneoMissingProductBehavior.Ignore => "Ignore missing products",
            AkeneoMissingProductBehavior.Unpublish => "Unpublish missing products",
            AkeneoMissingProductBehavior.DisablePurchasing => "Disable purchasing",
            AkeneoMissingProductBehavior.DetachFromParent => "Detach from parent",
            AkeneoMissingProductBehavior.SoftDelete => "Soft delete",
            AkeneoMissingProductBehavior.Delete => "Delete (destructive)",
            _ => behavior.ToString()
        };
    }

    private static string GetUpdatedFilterModeText(
        AkeneoUpdatedFilterMode mode)
    {
        return mode switch
        {
            //AkeneoUpdatedFilterMode.None =>
            //    "No updated-date filter",

            AkeneoUpdatedFilterMode.FixedDate =>
                "Updated after a fixed date",

            AkeneoUpdatedFilterMode.RollingDays =>
                "Updated within rolling days",

            AkeneoUpdatedFilterMode.SinceLastSuccessfulRun =>
                "Since last successful run",

            _ => mode.ToString()
        };
    }
}