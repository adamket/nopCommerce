using Nop.Core;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;

public class AkeneoAttributeMapping : BaseEntity
{
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
    public string Locale { get; set; }
    public string Channel { get; set; }
    public string TransformRuleJson { get; set; }
    public bool IsRequired { get; set; }
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
