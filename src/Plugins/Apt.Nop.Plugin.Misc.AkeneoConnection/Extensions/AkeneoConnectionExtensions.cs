using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Extensions;

public static class AkeneoConnectionExtensions
{
    public static AkeneoProductBatchImportRequest FromProfile(this AkeneoSyncProfile profile,
        int syncRunRecordId)
    {
        if (profile == null)
            throw new ArgumentNullException(nameof(profile));

        var categoryCodes = SplitCsv(profile.AkeneoCategoryCodes);

        if (!categoryCodes.Any() &&
            !string.IsNullOrWhiteSpace(profile.RootCategoryCode))
        {
            categoryCodes.Add(profile.RootCategoryCode.Trim());
        }

        var importMode = (AkeneoImportMode)profile.ImportModeId;

        return new AkeneoProductBatchImportRequest
        {
            SyncRunRecordId = syncRunRecordId,

            Channel = profile.AkeneoChannel,
            Locale = SplitCsv(profile.AkeneoLocales).FirstOrDefault(),

            PageSize = profile.PageSize <= 0 ? 100 : profile.PageSize,
            MaxProducts = profile.MaxProducts,
            ContinueOnError = profile.ContinueOnError,

            CreateNewProducts =
                importMode == AkeneoImportMode.CreateAndUpdate ||
                importMode == AkeneoImportMode.CreateOnly,

            UpdateExistingProducts =
                importMode == AkeneoImportMode.CreateAndUpdate ||
                importMode == AkeneoImportMode.UpdateOnly,

            AddMappedCategories = profile.AddMappedCategories,
            AddMappedManufacturers = profile.AddMappedManufacturers,
            CreateMissingSpecificationAttributeOptions = profile.CreateMissingSpecificationAttributeOptions,
            CreateMissingProductAttributeValues = profile.CreateMissingProductAttributeValues,
            SaveRawPayloadSnapshot = profile.SaveRawPayloadSnapshot,

            AkeneoCategoryCodes = categoryCodes,
            CategoryFilterMode = (AkeneoCategoryFilterMode)profile.CategoryFilterModeId,
            AkeneoFamilyCodes = SplitCsv(profile.AkeneoFamilyCodes),

            ProductEnabledFilter = (AkeneoProductEnabledFilter)profile.ProductEnabledFilterId,
            UpdatedAfterUtc = profile.UpdatedAfterUtc,
            UpdatedSinceLastNDays = profile.UpdatedSinceLastNDays,
            ProductParentFilterMode = (AkeneoProductParentFilterMode)profile.ProductParentFilterModeId,

            AdditionalSearchJson = profile.AdditionalSearchJson,
            UnmappedAttributeBehavior = (UnmappedAkeneoAttributeBehavior)profile.UnmappedAttributeBehaviorId
        };
    }

    public static List<string> SplitCsv(this string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new List<string>();

        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string BuildCsv(
        this IEnumerable<string> values)
    {
        if (values == null)
            return null;

        var items = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return items.Any()
            ? string.Join(",", items)
            : null;
    }

}