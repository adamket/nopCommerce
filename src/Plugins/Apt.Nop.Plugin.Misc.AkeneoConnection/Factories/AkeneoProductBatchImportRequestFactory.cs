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
     DateTime? previousSuccessfulWatermarkUtc = null,
     AkeneoRunMode runMode = AkeneoRunMode.Delta)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var productWriteMode =
            (AkeneoProductWriteMode)profile.ProductWriteModeId;

        var request = new AkeneoProductBatchImportRequest
        {
            SyncRunRecordId = syncRunRecordId,
            SyncProfileId = profile.Id,
            RunMode = runMode,
            ScopeHash = AkeneoSyncScopeHasher.Build(profile),

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

            AssetSyncMode =
                (AkeneoCollectionSyncMode)
                    profile.AssetSyncModeId,

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

            UpdatedAfterUtc = runMode == AkeneoRunMode.Full
                ? null
                : ResolveUpdatedAfterUtc(profile, previousSuccessfulWatermarkUtc),

            IncludeLinkedAssetUpdates =
                runMode != AkeneoRunMode.Full &&
                profile.IncludeLinkedAssetUpdates,

            ProductParentFilterMode =
                (AkeneoProductParentFilterMode)
                    profile.ProductParentFilterModeId,

            AdditionalSearchJson =
                profile.AdditionalSearchJson,

            UnmappedAttributeBehavior =
                (UnmappedAkeneoAttributeBehavior)
                    profile.UnmappedAttributeBehaviorId,

            MissingProductBehavior =
                (AkeneoMissingProductBehavior)
                    profile.MissingProductBehaviorId,
                
        };

        request.SearchJson = searchJsonBuilder.Build(request);

        return request;
    }

    private static DateTime? ResolveUpdatedAfterUtc(
        AkeneoSyncProfile profile,
        DateTime? previousSuccessfulWatermarkUtc)
    {
        return (AkeneoUpdatedFilterMode)profile.UpdatedFilterModeId switch
        {
            AkeneoUpdatedFilterMode.FixedDate =>
                profile.UpdatedAfterUtc,

            AkeneoUpdatedFilterMode.RollingDays
                when profile.UpdatedSinceLastNDays > 0 =>
                DateTime.UtcNow.AddDays(-profile.UpdatedSinceLastNDays.Value),

            AkeneoUpdatedFilterMode.SinceLastSuccessfulRun =>
                previousSuccessfulWatermarkUtc?.AddMinutes(-2),

            _ => null
        };
    }
}