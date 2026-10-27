using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;

public class AkeneoProductImportRequest
{
    public int SyncRunRecordId { get; set; }

    /// <summary>
    /// Database lease associated with this execution. Batch orchestration
    /// renews it at safe page/item boundaries so long-running jobs cannot be
    /// overtaken by another application instance.
    /// </summary>
    public int? SyncLeaseId { get; set; }

    public int? SyncProfileId { get; set; }

    public AkeneoRunMode RunMode { get; set; } = AkeneoRunMode.SingleProduct;

    public string ScopeHash { get; set; }

    public string AkeneoProductUuid { get; set; }

    public string AkeneoProductModelCode { get; set; }

    public string Locale { get; set; }

    public string Channel { get; set; }

    public string Currency { get; set; }

    public string SkipRulesJson { get; set; }


    public bool CreateNewProducts { get; set; } = true;

    public bool UpdateExistingProducts { get; set; } = true;

    public AkeneoMissingValueBehavior ProductFieldMissingValueBehavior { get; set; }
        = AkeneoMissingValueBehavior.PreserveExisting;

    public AkeneoMissingValueBehavior SeoFieldMissingValueBehavior { get; set; }
        = AkeneoMissingValueBehavior.PreserveExisting;

    public AkeneoMissingValueBehavior CustomPropertyMissingValueBehavior { get; set; }
        = AkeneoMissingValueBehavior.PreserveExisting;

    public AkeneoCollectionSyncMode CategorySyncMode { get; set; }
        = AkeneoCollectionSyncMode.Disabled;

    public AkeneoCollectionSyncMode TagSyncMode { get; set; }
        = AkeneoCollectionSyncMode.Disabled;

    public AkeneoCollectionSyncMode RelatedProductSyncMode { get; set; }
        = AkeneoCollectionSyncMode.Disabled;

    /// <summary>The Akeneo association type whose targets become nopCommerce related products.</summary>
    public string RelatedProductAssociationType { get; set; }

    public AkeneoCollectionSyncMode CrossSellSyncMode { get; set; }
        = AkeneoCollectionSyncMode.Disabled;

    /// <summary>The Akeneo association type whose targets become nopCommerce cross-sell products.</summary>
    public string CrossSellAssociationType { get; set; }

    public AkeneoCollectionSyncMode SpecificationAttributeSyncMode { get; set; }
        = AkeneoCollectionSyncMode.Merge;

    public AkeneoCollectionSyncMode ProductAttributeSyncMode { get; set; }
        = AkeneoCollectionSyncMode.Merge;

    public AkeneoCollectionSyncMode AssetSyncMode { get; set; }
        = AkeneoCollectionSyncMode.ReplaceManaged;

    public AkeneoMissingProductBehavior MissingProductBehavior { get; set; }
        = AkeneoMissingProductBehavior.Ignore;

    public bool CreateMissingProductAttributeValues { get; set; } = true;

    /// <summary>
    /// Resync only: write products whose desired-state hash is unchanged too,
    /// putting back destination edits made in nopCommerce.
    /// </summary>
    public bool RepairDestination { get; set; }

    public bool IsAuthoritativeFullRun =>
        SyncProfileId.HasValue &&
        SyncProfileId.Value > 0 &&
        RunMode == AkeneoRunMode.Full;
}
