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
}
