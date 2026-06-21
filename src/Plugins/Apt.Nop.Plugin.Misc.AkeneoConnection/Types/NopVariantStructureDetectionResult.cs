using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
public class NopVariantStructureDetectionResult
{
    public bool HasVariantStructure { get; init; }

    public bool IsAmbiguous { get; init; }

    public AkeneoVariantRelationshipMode? Mode { get; init; }

    public int? AssociatedProductAttributeId { get; init; }
}
