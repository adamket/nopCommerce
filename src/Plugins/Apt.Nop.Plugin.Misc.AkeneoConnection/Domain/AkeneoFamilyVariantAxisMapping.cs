using Nop.Core;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
public class AkeneoFamilyVariantAxisMapping : BaseEntity
{
    public int FamilyVariantImportConfigurationId { get; set; }

    public string AkeneoAttributeCode { get; set; }

    public int NopProductAttributeId { get; set; }

    public bool IsRequired { get; set; } = true;

    public int DisplayOrder { get; set; }

    public int AkeneoVariantAxisLevel { get; set; } = 1;
}