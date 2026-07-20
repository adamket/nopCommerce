using Nop.Core;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
public class AkeneoFamilyMapping : BaseEntity
{
    public string AkeneoFamilyCode { get; set; }

    public bool Enabled { get; set; } = true;

    public int VariantRelationshipModeId { get; set; }

    public int ProductModelHierarchyModeId { get; set; }

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

    public AkeneoProductModelHierarchyMode ProductModelHierarchyMode
    {
        get => (AkeneoProductModelHierarchyMode)ProductModelHierarchyModeId;
        set => ProductModelHierarchyModeId = (int)value;
    }
}



public enum AkeneoVariantRelationshipMode
{
    None = 0,

    ProductAttributeCombinations = 10,

    AssociatedToProductAttributeValue = 20,

    GroupedProducts = 30
}

/// <summary>
/// Controls which Akeneo product-model level becomes the nopCommerce parent.
/// </summary>
public enum AkeneoProductModelHierarchyMode
{
    /// <summary>
    /// Preserve the existing behavior: the leaf product's immediate product
    /// model becomes the nopCommerce parent product.
    /// </summary>
    ImmediateParentProductModel = 0,

    /// <summary>
    /// Walk to the root product model. Intermediate submodels contribute
    /// inherited values and variant axes, but are not imported as products.
    /// </summary>
    RootProductModel = 10
}

public enum AkeneoVariantRelationshipSource
{
    ExistingNopParent = 10,

    AkeneoFamilyConfiguration = 20
}