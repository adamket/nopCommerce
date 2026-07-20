using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;

/// <summary>
/// Centralizes attribute-mapping applicability rules so persistence, admin
/// configuration, previews, and synchronization interpret entity flags
/// consistently.
/// </summary>
public static class AkeneoAttributeMappingScopeHelper
{
    public static bool IsValidConfiguredScopeId(int entityScopeId)
    {
        var allowed = (int)AkeneoAttributeMappingEntityScope.All;

        return entityScopeId > 0 &&
               (entityScopeId & ~allowed) == 0;
    }

    public static int NormalizeConfiguredScopeId(int? entityScopeId)
    {
        return (int)NormalizeConfiguredScope(entityScopeId);
    }

    public static AkeneoAttributeMappingEntityScope NormalizeConfiguredScope(
        int? entityScopeId)
    {
        var normalized =
            (AkeneoAttributeMappingEntityScope)(entityScopeId.GetValueOrDefault() &
                                                (int)AkeneoAttributeMappingEntityScope.All);

        return normalized == AkeneoAttributeMappingEntityScope.None
            ? AkeneoAttributeMappingEntityScope.All
            : normalized;
    }

    public static AkeneoAttributeMappingEntityScope ResolveDefaultCurrentScope(
        AkeneoProductDefinition source,
        AkeneoEntityType sourceEntityType)
    {
        if (sourceEntityType == AkeneoEntityType.ProductModel)
            return AkeneoAttributeMappingEntityScope.ProductModel;

        return string.IsNullOrWhiteSpace(source?.Parent)
            ? AkeneoAttributeMappingEntityScope.StandaloneProduct
            : AkeneoAttributeMappingEntityScope.VariantProduct;
    }

    public static AkeneoAttributeMappingEntityScope NormalizeCurrentScope(
        AkeneoAttributeMappingEntityScope currentScope,
        AkeneoProductDefinition source,
        AkeneoEntityType sourceEntityType)
    {
        return currentScope is
            AkeneoAttributeMappingEntityScope.ProductModel or
            AkeneoAttributeMappingEntityScope.VariantProduct or
            AkeneoAttributeMappingEntityScope.StandaloneProduct
                ? currentScope
                : ResolveDefaultCurrentScope(source, sourceEntityType);
    }

    public static bool AppliesTo(
        AkeneoAttributeMapping mapping,
        AkeneoAttributeMappingEntityScope currentScope)
    {
        ArgumentNullException.ThrowIfNull(mapping);

        return NormalizeConfiguredScope(mapping.EntityScopeId)
            .HasFlag(currentScope);
    }
}
