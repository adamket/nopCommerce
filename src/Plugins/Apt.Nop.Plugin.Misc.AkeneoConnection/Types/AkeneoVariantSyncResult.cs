using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types;

public class AkeneoVariantSyncResult
{
    public AkeneoVariantRelationshipMode Mode { get; set; }

    public AkeneoVariantRelationshipSource Source { get; set; }

    public int? FamilyVariantImportConfigurationId { get; set; }

    public int? NopProductId { get; set; }

    public int? NopParentProductId { get; set; }

    public int? NopProductAttributeCombinationId { get; set; }

    public int? NopProductAttributeValueId { get; set; }

    public bool ParentChanged { get; set; }

    public bool RelationshipChanged { get; set; }

    public bool DestinationChanged { get; set; }

    public bool DestinationCreated { get; set; }

    public bool Changed => ParentChanged || RelationshipChanged || DestinationChanged;
}
