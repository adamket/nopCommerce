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
     AkeneoRunMode runMode = AkeneoRunMode.Delta,
     AkeneoResyncOptions resyncOptions = null)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var productWriteMode =
            (AkeneoProductWriteMode)profile.ProductWriteModeId;

        var resync = runMode == AkeneoRunMode.Resync
            ? resyncOptions ?? new AkeneoResyncOptions()
            : null;

        if (resync?.UpdateExistingOnly == true)
            productWriteMode = AkeneoProductWriteMode.UpdateOnly;

        var familyCodes = resync?.FamilyCodes?.Any(code => !string.IsNullOrWhiteSpace(code)) == true
            ? resync.FamilyCodes
            : profile.AkeneoFamilyCodes.SplitCsv();

        // Reject invalid persisted configuration before a run can write or reconcile.
        AkeneoProfileRules.ParseSkipRules(profile.SkipRulesJson);

        var valueContext = AkeneoValueContext.FromProfile(profile);

        var request = new AkeneoProductBatchImportRequest
        {
            SkipRulesJson = profile.SkipRulesJson,
            SyncRunRecordId = syncRunRecordId,
            SyncProfileId = profile.Id,
            RunMode = runMode,
            ScopeHash = AkeneoSyncScopeHasher.Build(profile, runMode),

            Channel = valueContext.Channel,
            Locale = valueContext.Locale,
            SecondaryLocales = profile.AkeneoLocales.SplitCsv()
                .Skip(1)
                .Where(locale => !string.Equals(locale, valueContext.Locale, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Currency = valueContext.Currency,

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

            TagSyncMode =
                (AkeneoCollectionSyncMode)
                    profile.TagSyncModeId,

            RelatedProductSyncMode =
                (AkeneoCollectionSyncMode)
                    profile.RelatedProductSyncModeId,

            RelatedProductAssociationType =
                profile.RelatedProductAssociationType?.Trim(),

            CrossSellSyncMode =
                (AkeneoCollectionSyncMode)
                    profile.CrossSellSyncModeId,

            CrossSellAssociationType =
                profile.CrossSellAssociationType?.Trim(),

            SpecificationAttributeSyncMode =
                (AkeneoCollectionSyncMode)
                    profile.SpecificationAttributeSyncModeId,

            ProductAttributeSyncMode =
                (AkeneoCollectionSyncMode)
                    profile.ProductAttributeSyncModeId,

            AssetSyncMode =
                (AkeneoCollectionSyncMode)
                    profile.AssetSyncModeId,

            CreateMissingProductAttributeValues =
                profile.CreateMissingProductAttributeValues,

            AkeneoCategoryCodes =
                profile.AkeneoCategoryCodes.SplitCsv(),

            CategoryFilterMode =
                (AkeneoCategoryFilterMode)
                    profile.CategoryFilterModeId,

            AkeneoFamilyCodes =
                familyCodes,

            AkeneoProductGroupCodes =
                profile.AkeneoProductGroupCodes.SplitCsv(),

            ProductEnabledFilter =
                (AkeneoProductEnabledFilter)
                    profile.ProductEnabledFilterId,

            CompletenessFilter = runMode == AkeneoRunMode.Full
                ? AkeneoCompletenessFilter.RequiredComplete
                : (AkeneoCompletenessFilter)profile.CompletenessFilterId,

            // Full and resync runs read every product in scope, so they need no
            // updated-date filter or linked-asset change search.
            UpdatedAfterUtc = runMode is AkeneoRunMode.Full or AkeneoRunMode.Resync
                ? null
                : ResolveUpdatedAfterUtc(profile, previousSuccessfulWatermarkUtc),

            IncludeLinkedAssetUpdates =
                runMode is not (AkeneoRunMode.Full or AkeneoRunMode.Resync) &&
                profile.IncludeLinkedAssetUpdates,

            RepairDestination = resync?.RepairDestination == true,

            ProductParentFilterMode =
                (AkeneoProductParentFilterMode)
                    profile.ProductParentFilterModeId,

            AdditionalSearchJson =
                profile.AdditionalSearchJson,

            UnmappedAttributeBehavior =
                UnmappedAkeneoAttributeBehaviorExtensions.FromId(
                    profile.UnmappedAttributeBehaviorId),

            MissingProductBehavior =
                (AkeneoMissingProductBehavior)
                    profile.MissingProductBehaviorId
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
