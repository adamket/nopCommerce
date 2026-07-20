
using Nop.Core.Caching;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection;
public static class AkeneoConnectionConstants
{
    public const string SystemName = "Apt.Nop.Plugin.Misc.AkeneoConnection";
    public const string TablePrefix = "Apt_";
    public const string PathToPlugin = "~/Plugins/Apt.Misc.AkeneoConnection";

    private const string EntityName = "Apt.akeneoattributemapping";

    public static CacheKey AttributeMappingsAllCacheKey => new($"{EntityName}.all");

    /// <summary>
    /// {0}: mapping id
    /// </summary>
    public static CacheKey AttributeMappingsByIdCacheKey => new($"{EntityName}.byid.{{0}}");

    /// <summary>
    /// {0}: family scope; {1}: Akeneo attribute code.
    /// </summary>
    public static CacheKey AttributeMappingsBySourceCacheKey =>
        new($"{EntityName}.bysource.{{0}}.{{1}}");

    public static string AttributeMappingPrefix => $"{EntityName}.";

    public static CacheKey AttributeMappingFallbackSourcesAllCacheKey =>
        new($"{EntityName}.fallbacksources.all");
    public static CacheKey EntityMappingByCodeCacheKey =>
        new("Apt.Nop.Plugin.Misc.AkeneoConnection.EntityMapping.type-{0}.nop-{1}.code-{2}");

    // Evicts every cached mapping for one Akeneo entity type. {0} = akeneoEntityTypeId
    public static string EntityMappingByAkeneoTypePrefix =>
        "Apt.Nop.Plugin.Misc.AkeneoConnection.EntityMapping.type-{0}.";

    // Evicts all cached mappings — for a plugin uninstall / full remap handler.
    public static string EntityMappingAllPrefix =>
        "Apt.Nop.Plugin.Misc.AkeneoConnection.EntityMapping.";


    /// <summary>
    /// {0}: reference entity code; {1}: record code.
    /// </summary>
    public static CacheKey ReferenceEntityRecordCacheKey =>
        new("Apt.Nop.Plugin.Misc.AkeneoConnection.ReferenceEntity.{0}.Record.{1}");


}
