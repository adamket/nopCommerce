using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types;

public class AkeneoVariantImportContext
{
    public string AkeneoIdentifier { get; set; }

    public string AkeneoUuid { get; set; }

    public string AkeneoFamilyCode { get; set; }

    public string AkeneoFamilyVariantCode { get; set; }

    public string Sku { get; set; }

    public int? StockQuantity { get; set; }

    public decimal? Price { get; set; }

    public int? SyncProfileId { get; set; }

    public int SyncRunRecordId { get; set; }

    public int? ExistingProductAttributeCombinationId { get; set; }

    public int? ExistingAssociatedProductAttributeValueId { get; set; }

    public AkeneoProductDefinition SourceProduct { get; set; }

    public string Locale { get; set; }

    public string Channel { get; set; }

    public string Currency { get; set; }

    // Null means the resolver decides from existing structure and family defaults.
    public AkeneoVariantRelationshipMode? VariantRelationshipModeOverride { get; set; }

    public IDictionary<string, AkeneoVariantAxisValue> AxisValuesByAkeneoCode { get; set; } =
        new Dictionary<string, AkeneoVariantAxisValue>(StringComparer.OrdinalIgnoreCase);

    public AkeneoVariantRelationshipOptions Options { get; set; }
}
