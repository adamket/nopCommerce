using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;

public class AkeneoProductImportRequest
{
    public int SyncRunRecordId { get; set; }

    public string AkeneoProductUuid { get; set; }

    public string Locale { get; set; }

    public string Channel { get; set; }

    public string Currency { get; set; }

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

    public AkeneoCollectionSyncMode SpecificationAttributeSyncMode { get; set; }
        = AkeneoCollectionSyncMode.Merge;

    public AkeneoCollectionSyncMode ProductAttributeSyncMode { get; set; }
        = AkeneoCollectionSyncMode.Merge;

    public bool CreateMissingSpecificationAttributeOptions { get; set; } = true;

    public bool CreateMissingProductAttributeValues { get; set; } = true;

    public bool SaveRawPayloadSnapshot { get; set; }
}