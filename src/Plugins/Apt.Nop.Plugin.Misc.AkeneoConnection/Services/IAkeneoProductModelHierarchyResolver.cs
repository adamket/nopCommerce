using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public interface IAkeneoProductModelHierarchyResolver
{
    AkeneoProductModelHierarchyResolution Resolve(
        AkeneoProductDefinition leaf,
        IReadOnlyList<AkeneoProductDefinition> ancestorsNearestFirst,
        AkeneoProductModelHierarchyMode mode);

    /// <summary>
    /// Makes the leaf's immediate parent model (a sub-model matched by a
    /// sub-model parent rule) the nopCommerce parent, merged with its own
    /// ancestors, whatever the family's hierarchy mode.
    /// </summary>
    AkeneoProductModelHierarchyResolution ResolveSubModelParent(
        AkeneoProductDefinition leaf,
        IReadOnlyList<AkeneoProductDefinition> ancestorsNearestFirst);
}
