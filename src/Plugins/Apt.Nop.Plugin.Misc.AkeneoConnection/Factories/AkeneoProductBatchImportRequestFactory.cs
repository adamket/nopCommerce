using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Extensions;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;
public class AkeneoProductBatchImportRequestFactory(
    AkeneoProductSearchJsonBuilder searchJsonBuilder)
    : IAkeneoProductBatchImportRequestFactory
{
    public AkeneoProductBatchImportRequest CreateFromProfile(
        AkeneoSyncProfile profile,
        int syncRunRecordId)
    {
        if (profile == null)
            throw new ArgumentNullException(nameof(profile));

        var categoryCodes = profile.AkeneoCategoryCodes.SplitCsv();

        if (!categoryCodes.Any() &&
            !string.IsNullOrWhiteSpace(profile.RootCategoryCode))
        {
            categoryCodes.Add(profile.RootCategoryCode.Trim());
        }

        var importMode = (AkeneoImportMode)profile.ImportModeId;

        var request = new AkeneoProductBatchImportRequest
        {
            SyncRunRecordId = syncRunRecordId,

            Channel = profile.AkeneoChannel,
            Locale = profile.AkeneoLocales.SplitCsv().FirstOrDefault(),

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

            CreateMissingSpecificationAttributeOptions =
                profile.CreateMissingSpecificationAttributeOptions,

            CreateMissingProductAttributeValues =
                profile.CreateMissingProductAttributeValues,

            SaveRawPayloadSnapshot = profile.SaveRawPayloadSnapshot,

            AkeneoCategoryCodes = categoryCodes,
            CategoryFilterMode = (AkeneoCategoryFilterMode)profile.CategoryFilterModeId,

            AkeneoFamilyCodes = profile.AkeneoFamilyCodes.SplitCsv(),

            ProductEnabledFilter =
                (AkeneoProductEnabledFilter)profile.ProductEnabledFilterId,

            UpdatedAfterUtc = profile.UpdatedAfterUtc,
            UpdatedSinceLastNDays = profile.UpdatedSinceLastNDays,

            ProductParentFilterMode =
                (AkeneoProductParentFilterMode)profile.ProductParentFilterModeId,

            AdditionalSearchJson = profile.AdditionalSearchJson,

            UnmappedAttributeBehavior =
                (UnmappedAkeneoAttributeBehavior)profile.UnmappedAttributeBehaviorId
        };

        request.SearchJson = searchJsonBuilder.Build(request);

        return request;
    }
}