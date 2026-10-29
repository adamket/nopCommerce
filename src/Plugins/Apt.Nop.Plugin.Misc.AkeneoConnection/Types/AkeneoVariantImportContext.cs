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

    public bool ManageVariantsByEnabledStatus { get; set; }

    // Only read when ManageVariantsByEnabledStatus is on.
    public AkeneoCombinationAvailabilitySource CombinationAvailabilitySource { get; set; }

    // The variant's computed Published value, for the Published rule source: true or false, or
    // held (its mapping was skipped by "Apply only when"), or a problem that blocks the change.
    public bool? PublishedRuleResult { get; set; }

    public bool PublishedRuleHeld { get; set; }

    public string PublishedRuleProblem { get; set; }

    // Null means the resolver decides from existing structure and family defaults.
    public AkeneoVariantRelationshipMode? VariantRelationshipModeOverride { get; set; }

    // A sub-model parent rule: its associated attribute, value name template and
    // child visibility replace the family's.
    public AkeneoFamilySubModelRule SubModelParentRule { get; set; }

    // Set from the family's variant display order keys; null keeps the order values were created in.
    public AkeneoVariantDisplayOrder DisplayOrder { get; set; }

    public IDictionary<string, AkeneoVariantAxisValue> AxisValuesByAkeneoCode { get; set; } =
        new Dictionary<string, AkeneoVariantAxisValue>(StringComparer.OrdinalIgnoreCase);

    public AkeneoVariantRelationshipOptions Options { get; set; }
}
