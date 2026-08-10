using Nop.Core;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;

public class AkeneoAttributeMapping : BaseEntity
{
    /// <summary>
    /// Stable slot key for computed mappings. A family-scoped row with the same
    /// key overrides the global computed mapping without changing its identity.
    /// </summary>
    public string MappingKey { get; set; }

    /// <summary>
    /// Administrator-facing name for a computed mapping.
    /// </summary>
    public string Name { get; set; }

    public int ValueModeId { get; set; } =
        (int)AkeneoAttributeMappingValueMode.SingleAttribute;

    public string ValueTemplate { get; set; }

    public AkeneoAttributeMappingValueMode ValueMode
    {
        get => (AkeneoAttributeMappingValueMode)ValueModeId;
        set => ValueModeId = (int)value;
    }

    public string AkeneoFamilyCode { get; set; }
    public string AkeneoAttributeCode { get; set; }
    public int AkeneoAttributeTypeId { get; set; }

    /// <summary>
    /// The reference entity linked by the Akeneo product attribute, when the
    /// source attribute is a reference-entity single or multiple link.
    /// </summary>
    public string AkeneoReferenceEntityCode { get; set; }

    /// <summary>
    /// The field on the linked reference entity record whose value should be
    /// mapped to nopCommerce. "@code" maps the record code itself.
    /// </summary>
    public string AkeneoReferenceEntityAttributeCode { get; set; }

    public int NopTargetTypeId { get; set; }
    public int? NopTargetEntityId { get; set; }
    public string NopTargetKey { get; set; }

    /// <summary>
    /// Controls how a missing nopCommerce specification option is handled for
    /// this mapping. Existing matching options are always reused.
    /// </summary>
    public int SpecificationMissingValueHandlingId { get; set; } =
        (int)AkeneoSpecificationMissingValueHandling.CreateSpecificationAttributeOption;

    public AkeneoSpecificationMissingValueHandling SpecificationMissingValueHandling
    {
        get => (AkeneoSpecificationMissingValueHandling)
            SpecificationMissingValueHandlingId;
        set => SpecificationMissingValueHandlingId = (int)value;
    }

    public string Locale { get; set; }
    public string Channel { get; set; }
    public string TransformRuleJson { get; set; }
    public bool IsRequired { get; set; }

    /// <summary>
    /// Flags describing which nopCommerce product roles this mapping applies to.
    /// Existing mappings default to all roles for backward compatibility.
    /// </summary>
    public int EntityScopeId { get; set; } =
        (int)AkeneoAttributeMappingEntityScope.All;

    public AkeneoAttributeMappingEntityScope EntityScope
    {
        get => (AkeneoAttributeMappingEntityScope)EntityScopeId;
        set => EntityScopeId = (int)value;
    }
}


public enum AkeneoAttributeMappingValueMode
{
    /// <summary>
    /// Resolve one primary Akeneo attribute and its ordered fallback sources.
    /// </summary>
    SingleAttribute = 0,

    /// <summary>
    /// Render one destination value from a deterministic token template.
    /// </summary>
    Template = 10
}

public enum AkeneoSpecificationMissingValueHandling
{
    /// <summary>
    /// Create a missing nopCommerce SpecificationAttributeOption and assign it
    /// to the product.
    /// </summary>
    CreateSpecificationAttributeOption = 0,

    /// <summary>
    /// Store the Akeneo display value in a CustomText
    /// ProductSpecificationAttribute instead of creating a reusable option.
    /// </summary>
    UseProductSpecificationCustomValue = 10,

    /// <summary>
    /// Leave the missing value unassigned and report it for review.
    /// </summary>
    SkipMissingValue = 20
}

[Flags]
public enum AkeneoAttributeMappingEntityScope
{
    None = 0,

    /// <summary>
    /// nopCommerce parent products synchronized from Akeneo product models.
    /// </summary>
    ProductModel = 1,

    /// <summary>
    /// Real nopCommerce child products representing Akeneo variant products.
    /// </summary>
    VariantProduct = 2,

    /// <summary>
    /// nopCommerce products imported without a parent relationship.
    /// </summary>
    StandaloneProduct = 4,

    All = ProductModel | VariantProduct | StandaloneProduct
}

public enum AkeneoAttributeType
{
    Unknown = 0,
    Text = 10,
    TextArea = 20,
    Number = 30,
    Price = 40,
    Date = 50,
    DateTime = 60,
    Boolean = 70,
    Select = 80,
    MultiSelect = 90,
    Identifier = 100,
    Image = 110,
    File = 120,
    Metric = 130,
    ReferenceEntity = 140,
    ReferenceEntityCollection = 150
}

public enum NopTargetType
{
    Ignore = 0,
    ProductField = 10,
    ProductAttribute = 20,
    SpecificationAttribute = 30,
    Manufacturer = 40,
    Category = 50,
    SeoField = 60,
    CustomProperty = 70
}
