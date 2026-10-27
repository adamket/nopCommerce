namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
public class AkeneoFamilySubModelRuleModel
{
    public int Id { get; set; }
    public int FamilyMappingId { get; set; }

    public string AkeneoAxisAttributeCode { get; set; }
    public string TriggerValue { get; set; }
    public string VariantAxisAttributeCode { get; set; }
    public string VariantTriggerValue { get; set; }

    public int VariantRelationshipOverrideModeId { get; set; }
    public bool MergeAncestorValues { get; set; } = true;
    public int DisplayOrder { get; set; }

    public int ParentLevelId { get; set; }
    public int? AssociatedProductAttributeId { get; set; }
    public string AssociatedValueNameTemplate { get; set; }
    public bool HideChildProducts { get; set; } = true;
}
