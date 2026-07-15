using Nop.Core;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
public class AkeneoAttributeMapping : BaseEntity
{
    public string AkeneoFamilyCode { get; set; }
    public string AkeneoAttributeCode { get; set; }
    public int AkeneoAttributeTypeId { get; set; }
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
    ReferenceEntity = 140
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
    CustomProperty = 70,

}


