using Nop.Core;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
public class AkeneoFamilyVariantImportConfiguration : BaseEntity
{
    public string AkeneoFamilyCode { get; set; }

    public bool Enabled { get; set; } = true;

    public int VariantRelationshipModeId { get; set; }

    public bool PreserveExistingNopVariantStructure { get; set; } = true;

    public int? AssociatedProductAttributeId { get; set; }

    public string AssociatedValueNameTemplate { get; set; } = "{axes}";

    public bool HideChildProductsWhenRepresentedByParent { get; set; } = true;

    public int DisplayOrder { get; set; }

    public DateTime CreatedOnUtc { get; set; }

    public DateTime UpdatedOnUtc { get; set; }

    public AkeneoVariantRelationshipMode VariantRelationshipMode
    {
        get => (AkeneoVariantRelationshipMode)VariantRelationshipModeId;
        set => VariantRelationshipModeId = (int)value;
    }
}



public enum AkeneoVariantRelationshipMode
{
    None = 0,

    ProductAttributeCombinations = 10,

    AssociatedToProductAttributeValue = 20,

    GroupedProducts = 30
}

public enum AkeneoVariantRelationshipSource
{
    ExistingNopParent = 10,

    AkeneoFamilyConfiguration = 20
}