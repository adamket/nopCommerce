using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
public class AkeneoVariantImportResult
{
    public AkeneoVariantRelationshipMode Mode { get; set; }

    public AkeneoVariantRelationshipSource Source { get; set; }

    public int? FamilyVariantImportConfigurationId { get; set; }

    public int? NopProductId { get; set; }

    public int? NopProductAttributeCombinationId { get; set; }
}
