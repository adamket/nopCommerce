using Nop.Core;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
public class AkeneoFamilyMapping : BaseEntity
{
    public string AkeneoFamilyCode { get; set; }

    public string AkeneoFamilyVariantCode { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public bool ManageVariantsByEnabledStatus { get; set; }

    // What decides a managed combination's availability; only read when
    // ManageVariantsByEnabledStatus is on (it has kept its name since it was the only source).
    public int CombinationAvailabilitySourceId { get; set; }

    public int VariantRelationshipModeId { get; set; }

    public int ProductModelHierarchyModeId { get; set; }

    public bool PreserveExistingNopVariantStructure { get; set; } = true;

    public int? AssociatedProductAttributeId { get; set; }

    public string AssociatedValueNameTemplate { get; set; } = "{axes}";

    // Comma-separated sort keys ("root_type.display_order, container_size, pack_quantity")
    // that set the display order of variant values; empty leaves values in the order
    // they were first created.
    public string VariantDisplayOrderKeys { get; set; }

    public bool HideChildProductsWhenRepresentedByParent { get; set; } = true;

    // Commerce settings applied only when the sync creates a product in this
    // family (parents, standalone and child products); never on updates.
    // Null keeps the nopCommerce default.
    public int? NewProductTaxCategoryId { get; set; }

    public bool NewProductIsShipEnabled { get; set; }

    public int? NewProductWarehouseId { get; set; }

    public int? NewProductTemplateId { get; set; }

    public int DisplayOrder { get; set; }

    public DateTime CreatedOnUtc { get; set; }

    public DateTime UpdatedOnUtc { get; set; }

    public AkeneoVariantRelationshipMode VariantRelationshipMode
    {
        get => (AkeneoVariantRelationshipMode)VariantRelationshipModeId;
        set => VariantRelationshipModeId = (int)value;
    }

    public AkeneoCombinationAvailabilitySource CombinationAvailabilitySource
    {
        get => (AkeneoCombinationAvailabilitySource)CombinationAvailabilitySourceId;
        set => CombinationAvailabilitySourceId = (int)value;
    }

    public AkeneoProductModelHierarchyMode ProductModelHierarchyMode
    {
        get => (AkeneoProductModelHierarchyMode)ProductModelHierarchyModeId;
        set => ProductModelHierarchyModeId = (int)value;
    }
}



/// <summary>What decides whether a managed attribute combination exists.</summary>
public enum AkeneoCombinationAvailabilitySource
{
    /// <summary>The variant's Akeneo enabled status.</summary>
    EnabledStatus = 0,

    /// <summary>
    /// The variant's computed Published mapping (Variant product role): false removes the
    /// combination, true creates it, a held (skipped) mapping leaves it as it is.
    /// </summary>
    PublishedRule = 10
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
