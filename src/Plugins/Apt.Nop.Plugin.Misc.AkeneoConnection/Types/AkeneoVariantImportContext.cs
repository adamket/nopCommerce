using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types;

public class AkeneoVariantImportContext
{
    public string AkeneoIdentifier { get; set; }
    public string AkeneoFamilyCode { get; set; }
    public string Sku { get; set; }
    public int? StockQuantity { get; set; }
    public decimal? Price { get; set; }

    public AkeneoProductDefinition SourceProduct { get; set; }

    public string Locale { get; set; }

    public string Channel { get; set; }

    public string Currency { get; set; }

    // Null => resolver decides (existing structure, else family default).
    // Set => force this mode when CREATING a new product.
    public AkeneoVariantRelationshipMode? VariantRelationshipModeOverride { get; set; }

    public IDictionary<string, string> AxisValuesByAkeneoCode { get; set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public AkeneoVariantRelationshipOptions Options { get; set; }
}
