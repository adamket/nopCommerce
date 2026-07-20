using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types;

/// <summary>
/// Describes the product-model hierarchy selected for one Akeneo leaf.
/// </summary>
public sealed class AkeneoProductModelHierarchyResolution
{
    public AkeneoProductModelHierarchyMode Mode { get; init; }

    public AkeneoProductDefinition ImmediateParentModel { get; init; }

    public AkeneoProductDefinition ParentProductModel { get; init; }

    public AkeneoProductDefinition EffectiveParentProductModel { get; init; }

    public AkeneoProductDefinition LeafWithInheritedValues { get; init; }

    public AkeneoProductDefinition EffectiveLeaf { get; init; }

    public IReadOnlyList<AkeneoProductDefinition> AncestorsNearestFirst { get; init; } =
        Array.Empty<AkeneoProductDefinition>();

    public bool IsFlattened { get; init; }
}
