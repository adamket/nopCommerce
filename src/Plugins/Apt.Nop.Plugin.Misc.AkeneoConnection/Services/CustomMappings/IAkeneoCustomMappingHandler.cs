using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services.Planning;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services.CustomMappings;

/// <summary>
/// A compiled mapping target. PlanAsync must not write: attach writes to plan
/// operations, executed only by the core executor. Register implementations in DI.
/// </summary>
public interface IAkeneoCustomMappingHandler
{
    string Key { get; }
    string DisplayName { get; }
    IReadOnlyCollection<string> SupportedAkeneoTypes { get; }
    IEnumerable<string> Validate(AkeneoAttributeMapping mapping, AkeneoAttributeDefinition attribute);

    /// <summary>
    /// The mapping's NopTargetEntityId names the specification attribute this
    /// handler writes: the mapping page shows a picker for it, and no plain
    /// specification mapping may target the same attribute.
    /// </summary>
    bool TargetsSpecificationAttribute => false;

    Task PlanAsync(AkeneoCustomMappingContext context, AkeneoExecutableSectionPlan plan,
        CancellationToken cancellationToken = default);
}
public sealed class AkeneoCustomMappingContext
{
    public AkeneoProductSyncContext ProductContext { get; init; }
    public AkeneoResolvedMappedValue MappedValue { get; init; }
    public AkeneoAttributeDefinition Attribute { get; init; }
}
