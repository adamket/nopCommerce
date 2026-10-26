
using Nop.Core.Caching;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection;
public static class AkeneoConnectionConstants
{
    public const string SystemName = "Apt.Nop.Plugin.Misc.AkeneoConnection";
    public const string ExternalAssetHttpClientName = SystemName + ".ExternalAssets";
    public const string TablePrefix = "Apt_";
    public const string PathToPlugin = "~/Plugins/Apt.Misc.AkeneoConnection";

    /// <summary>
    /// Lease key that serializes every catalog writer (profile syncs and
    /// catalog binding).
    /// </summary>
    public const string CatalogWriterLockKey = "akeneo-catalog-writer";

    public const string SyncRunAlertMessageTemplateName = "Akeneo.SyncRun.Alert";
    public const string StaleSyncAlertMessageTemplateName = "Akeneo.StaleSync.Alert";

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

    private const string AssetMappingEntityName = "Apt.akeneoassetmapping";
    public static CacheKey AssetMappingsAllCacheKey => new($"{AssetMappingEntityName}.all");
    public static CacheKey AssetMappingsByIdCacheKey => new($"{AssetMappingEntityName}.byid.{{0}}");
    public static string AssetMappingPrefix => $"{AssetMappingEntityName}.";
    public static CacheKey EntityMappingByCodeCacheKey =>
        new("Apt.Nop.Plugin.Misc.AkeneoConnection.EntityMapping.type-{0}.nop-{1}.code-{2}");

    // Evicts every cached mapping for one Akeneo entity type. {0} = akeneoEntityTypeId
    public static string EntityMappingByAkeneoTypePrefix =>
        "Apt.Nop.Plugin.Misc.AkeneoConnection.EntityMapping.type-{0}.";

    // Evicts all cached mappings — for a plugin uninstall / full remap handler.
    public static string EntityMappingAllPrefix =>
        "Apt.Nop.Plugin.Misc.AkeneoConnection.EntityMapping.";



}
