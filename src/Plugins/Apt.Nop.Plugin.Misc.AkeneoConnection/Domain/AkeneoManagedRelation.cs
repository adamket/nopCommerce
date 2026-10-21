using Nop.Core;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;

/// <summary>
/// Identifies a product relationship that this plugin is allowed to remove.
/// Manual nopCommerce relationships are intentionally not claimed.
/// </summary>
public class AkeneoManagedRelation : BaseEntity
{
    public int SyncProfileId { get; set; }

    public int NopProductId { get; set; }

    public int RelationTypeId { get; set; }

    public int NopRelationEntityId { get; set; }

    public string AkeneoAttributeCode { get; set; }

    public string AkeneoValueCode { get; set; }

    public int LastSeenRunRecordId { get; set; }

    public DateTime CreatedOnUtc { get; set; }

    public DateTime UpdatedOnUtc { get; set; }
}

public enum AkeneoManagedRelationType
{
    ProductCategory = 10,
    ProductSpecificationAssignment = 20,
    ProductAttributeMapping = 30,
    ProductAttributeValue = 40,
    ProductAttributeCombination = 50,
    AssociatedProductAttributeValue = 60,
    GroupedProductRelationship = 70,
    TierPrice = 80,

    /// <summary>A product–tag link; the relation entity is the Product_ProductTag_Mapping row.</summary>
    ProductTag = 90,

    /// <summary>A related product link; the relation entity is the RelatedProduct row.</summary>
    RelatedProduct = 100
}
