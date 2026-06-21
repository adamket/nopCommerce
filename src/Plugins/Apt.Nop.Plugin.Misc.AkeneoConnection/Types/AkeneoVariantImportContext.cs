namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types;

public class AkeneoVariantImportContext
{
    public string AkeneoIdentifier { get; set; }

    public string AkeneoFamilyCode { get; set; }

    public string Sku { get; set; }

    public int StockQuantity { get; set; }

    public decimal? Price { get; set; }

    public IDictionary<string, string> AxisValuesByAkeneoCode { get; set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public AkeneoVariantRelationshipOptions Options { get; set; }
}

