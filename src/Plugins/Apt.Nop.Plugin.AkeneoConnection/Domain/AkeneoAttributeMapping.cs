using Nop.Core;

namespace Apt.Nop.Plugin.AkeneoConnection.Domain;
public class AkeneoAttributeMapping : BaseEntity
{
    public string AkeneoAttributeCode { get; set; }
    // public AkeneoAttributeType AkeneoAttributeType { get; set; }
    // NopTargetType // ProductField, ProductAttribute, SpecificationAttribute, Manufacturer, Category, SeoField, CustomProperty
    public string NopTargetKey { get; set; } // Name, ShortDescription, FullDescription, Sku, Price, Gtin, etc.
    public string Locale { get; set; }
    public string Channel { get; set; }
    public string TransformRuleJson { get; set; }
    public bool IsRequired { get; set; }


}




