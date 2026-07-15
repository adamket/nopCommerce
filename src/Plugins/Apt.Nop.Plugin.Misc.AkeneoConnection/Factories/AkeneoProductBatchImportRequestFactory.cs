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
     int syncRunRecordId,
     DateTime? lastSuccessfulRunStartedOnUtc = null)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var productWriteMode =
            (AkeneoProductWriteMode)profile.ProductWriteModeId;

        var request = new AkeneoProductBatchImportRequest
        {
            SyncRunRecordId = syncRunRecordId,

            Channel = profile.AkeneoChannel,

            Locale = profile.AkeneoLocales
                .SplitCsv()
                .FirstOrDefault() ?? "en_US",

            Currency = string.IsNullOrWhiteSpace(profile.CurrencyCode)
                ? "USD"
                : profile.CurrencyCode.Trim(),

            PageSize = profile.PageSize <= 0
                ? 100
                : profile.PageSize,

            MaxProducts = profile.MaxProducts,
            ContinueOnError = profile.ContinueOnError,

            CreateNewProducts =
                productWriteMode == AkeneoProductWriteMode.CreateAndUpdate ||
                productWriteMode == AkeneoProductWriteMode.CreateOnly,

            UpdateExistingProducts =
                productWriteMode == AkeneoProductWriteMode.CreateAndUpdate ||
                productWriteMode == AkeneoProductWriteMode.UpdateOnly,

            ProductFieldMissingValueBehavior =
                (AkeneoMissingValueBehavior)
                    profile.ProductFieldMissingValueBehaviorId,

            SeoFieldMissingValueBehavior =
                (AkeneoMissingValueBehavior)
                    profile.SeoFieldMissingValueBehaviorId,

            CustomPropertyMissingValueBehavior =
                (AkeneoMissingValueBehavior)
                    profile.CustomPropertyMissingValueBehaviorId,

            CategorySyncMode =
                (AkeneoCollectionSyncMode)
                    profile.CategorySyncModeId,

            SpecificationAttributeSyncMode =
                (AkeneoCollectionSyncMode)
                    profile.SpecificationAttributeSyncModeId,

            ProductAttributeSyncMode =
                (AkeneoCollectionSyncMode)
                    profile.ProductAttributeSyncModeId,

            CreateMissingSpecificationAttributeOptions =
                profile.CreateMissingSpecificationAttributeOptions,

            CreateMissingProductAttributeValues =
                profile.CreateMissingProductAttributeValues,

            SaveRawPayloadSnapshot =
                profile.SaveRawPayloadSnapshot,

            AkeneoCategoryCodes =
                profile.AkeneoCategoryCodes.SplitCsv(),

            CategoryFilterMode =
                (AkeneoCategoryFilterMode)
                    profile.CategoryFilterModeId,

            AkeneoFamilyCodes =
                profile.AkeneoFamilyCodes.SplitCsv(),

            AkeneoProductGroupCodes =
                profile.AkeneoProductGroupCodes.SplitCsv(),

            ProductEnabledFilter =
                (AkeneoProductEnabledFilter)
                    profile.ProductEnabledFilterId,

            UpdatedAfterUtc =
                ResolveUpdatedAfterUtc(profile, lastSuccessfulRunStartedOnUtc),

            ProductParentFilterMode =
                (AkeneoProductParentFilterMode)
                    profile.ProductParentFilterModeId,

            AdditionalSearchJson =
                profile.AdditionalSearchJson,

            UnmappedAttributeBehavior =
                (UnmappedAkeneoAttributeBehavior)
                    profile.UnmappedAttributeBehaviorId,
                
        };

        request.SearchJson = searchJsonBuilder.Build(request);

        return request;
    }

    private static DateTime? ResolveUpdatedAfterUtc(
        AkeneoSyncProfile profile,
        DateTime? lastSuccessfulRunStartedOnUtc)
    {
        return (AkeneoUpdatedFilterMode)profile.UpdatedFilterModeId switch
        {
            AkeneoUpdatedFilterMode.FixedDate =>
                profile.UpdatedAfterUtc,

            AkeneoUpdatedFilterMode.RollingDays
                when profile.UpdatedSinceLastNDays > 0 =>
                DateTime.UtcNow.AddDays(-profile.UpdatedSinceLastNDays.Value),

            AkeneoUpdatedFilterMode.SinceLastSuccessfulRun =>
                lastSuccessfulRunStartedOnUtc,   // null on first run => full sync, correct

            _ => null
        };
    }
}