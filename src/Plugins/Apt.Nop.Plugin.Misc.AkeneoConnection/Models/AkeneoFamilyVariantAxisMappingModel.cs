using Nop.Web.Framework.Models;
namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Models;

public record AkeneoFamilyVariantAxisMappingModel : BaseNopEntityModel
{
    public int FamilyVariantImportConfigurationId { get; set; }

    public string AkeneoAttributeCode { get; set; }

    public int NopProductAttributeId { get; set; }

    public bool IsRequired { get; set; } = true;

    public int DisplayOrder { get; set; }

    public int AkeneoVariantAxisLevel { get; set; } = 1;
}
