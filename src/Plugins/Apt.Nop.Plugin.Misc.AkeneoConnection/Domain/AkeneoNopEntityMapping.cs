using Nop.Core;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
public class AkeneoNopEntityMapping : BaseEntity
{
    public int AkeneoEntityTypeId { get; set; }
    public string AkeneoCode { get; set; }
    public string AkeneoUuid { get; set; }
    public int NopEntityId { get; set; }
    public int NopEntityTypeId { get; set; }
    public DateTime CreatedOnUtc { get; set; }
    public DateTime UpdatedOnUtc { get; set; }
}

public enum AkeneoEntityType
{
    Product = 10,
    ProductModel = 20,
    Category = 30,
    Attribute = 40,
    Family = 50,
    Asset = 60,
    Option = 70
}

public enum NopEntityType
{
    Product = 10,
    Category = 20,
    Manufacturer = 30,
    SpecificationAttribute = 40,
    ProductAttribute = 50,
    AttributeOption = 55,
    Picture = 60,
    SpecificationAttributeOption = 70,
    ProductAttributeMapping = 80,
    ProductAttributeValue = 90,
}
