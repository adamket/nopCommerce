namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
public enum AkeneoAttributeMappingTargetType
{
    Ignore = 0,

    ProductSku = 10,
    ProductName = 20,
    ProductShortDescription = 30,
    ProductFullDescription = 40,
    ProductPrice = 50,
    ProductPublished = 60,

    Manufacturer = 100,
    Category = 110,
    ProductTag = 120,

    SpecificationAttribute = 200,
    ProductAttribute = 210,

    Picture = 300,
    PictureAltText = 310,

    CustomField = 900
}