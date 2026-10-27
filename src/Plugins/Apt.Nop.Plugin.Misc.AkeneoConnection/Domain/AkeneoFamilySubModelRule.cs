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

    // Which Akeneo level becomes the nopCommerce parent for matching leaves.
    // MatchingSubModel makes the matched sub-model the parent, with each leaf
    // as an associated-to-product attribute value; the settings below apply then.
    public int ParentLevelId { get; set; }
    public int? AssociatedProductAttributeId { get; set; }
    public string AssociatedValueNameTemplate { get; set; }
    public bool HideChildProducts { get; set; } = true;

    public AkeneoVariantRelationshipMode VariantRelationshipOverrideMode
    {
        get => (AkeneoVariantRelationshipMode)VariantRelationshipOverrideModeId;
        set => VariantRelationshipOverrideModeId = (int)value;
    }

    public AkeneoSubModelParentLevel ParentLevel
    {
        get => (AkeneoSubModelParentLevel)ParentLevelId;
        set => ParentLevelId = (int)value;
    }
}

public enum AkeneoSubModelParentLevel
{
    /// <summary>The family mapping's product model hierarchy chooses the parent.</summary>
    FamilySetting = 0,

    /// <summary>
    /// The matched sub-model, merged with its ancestors, becomes the nopCommerce
    /// parent and each matching leaf an associated-to-product attribute value.
    /// </summary>
    MatchingSubModel = 10
}

public static class AkeneoFamilySubModelRuleExtensions
{
    public static bool UsesSubModelParent(this AkeneoFamilySubModelRule rule) =>
        rule?.ParentLevel == AkeneoSubModelParentLevel.MatchingSubModel;
}

