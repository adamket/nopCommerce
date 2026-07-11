using Nop.Core;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;

public class AkeneoFamilySubModelRule : BaseEntity
{
    public int FamilyMappingId { get; set; }

    // Sub-model-level condition (level-1 axis, e.g. "product_form" = "seedling").
    // Evaluated against the leaf's parent product model. Optional — blank means "ignore this level".
    public string AkeneoAxisAttributeCode { get; set; }
    public string TriggerValue { get; set; }

    // NEW — variant-level condition (level-2 axis, e.g. "size" = "seedling").
    // Evaluated against the leaf variant itself. Optional — blank means "ignore this level".
    public string VariantAxisAttributeCode { get; set; }
    public string VariantTriggerValue { get; set; }

    public int VariantRelationshipOverrideModeId { get; set; }
    public bool MergeAncestorValues { get; set; } = true;
    public int DisplayOrder { get; set; }
    public DateTime CreatedOnUtc { get; set; }
    public DateTime UpdatedOnUtc { get; set; }

    public AkeneoVariantRelationshipMode VariantRelationshipOverrideMode
    {
        get => (AkeneoVariantRelationshipMode)VariantRelationshipOverrideModeId;
        set => VariantRelationshipOverrideModeId = (int)value;
    }
}


