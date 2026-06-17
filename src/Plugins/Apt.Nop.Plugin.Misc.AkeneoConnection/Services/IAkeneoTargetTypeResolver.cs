using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public interface IAkeneoTargetTypeResolver
{
    /// <summary>Maps the raw Akeneo attribute type string to the plugin's AkeneoAttributeType enum.</summary>
    AkeneoAttributeType ResolveAkeneoAttributeType(AkeneoAttributeDefinition attribute);

    /// <summary>The default nop target type this attribute should map to before any user override.</summary>
    NopTargetType ResolveDefaultTargetType(AkeneoAttributeDefinition attribute);

    /// <summary>The target types a user is allowed to pick for this attribute.</summary>
    IReadOnlyList<NopTargetType> GetAllowedTargetTypes(AkeneoAttributeDefinition attribute);

    /// <summary>The default target key (e.g. "Sku") for the given attribute + chosen target type.</summary>
    string ResolveDefaultTargetKey(AkeneoAttributeDefinition attribute, NopTargetType targetType);

    /// <summary>The target-key options for a single target type (empty if the type has none).</summary>
    IReadOnlyList<NopTargetKeyOption> GetTargetKeyOptions(NopTargetType targetType);

    /// <summary>All target-key options, keyed by target type. Single source of truth for the client map.</summary>
    IReadOnlyDictionary<NopTargetType, IReadOnlyList<NopTargetKeyOption>> TargetKeyOptions { get; }
}