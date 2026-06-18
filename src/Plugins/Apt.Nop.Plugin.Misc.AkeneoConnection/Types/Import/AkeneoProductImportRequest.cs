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

    public bool CreateMissingSpecificationAttributeOptions { get; set; } = true;

    public bool CreateMissingProductAttributeValues { get; set; } = true;

    public bool AddMappedCategories { get; set; } = true;

    public bool AddMappedManufacturers { get; set; } = true;

    public bool SaveRawPayloadSnapshot { get; set; }
}