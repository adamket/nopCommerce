using Nop.Core.Caching;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public interface IAkeneoAttributeValueCacheTracker
{
    /// <summary>Records an attribute whose values the current unit wrote.</summary>
    void Touch(int productAttributeMappingId);

    /// <summary>Clears the cached values of every attribute touched since the last clear.</summary>
    Task ClearTouchedAsync();
}

/// <summary>
/// nopCommerce clears an attribute's cached values as soon as a value is
/// written, which inside a sync unit is before the commit. On a database with
/// read-committed snapshot (Azure SQL's default) a page read in between gets
/// the last committed values and caches them for the default cache time, and a
/// rolled-back unit can leave values it read cached. The sync calls
/// <see cref="ClearTouchedAsync"/> once each unit's transaction has finished.
/// </summary>
public class AkeneoAttributeValueCacheTracker(IStaticCacheManager staticCacheManager)
    : IAkeneoAttributeValueCacheTracker
{
    private readonly HashSet<int> _touched = new();

    public void Touch(int productAttributeMappingId)
    {
        if (productAttributeMappingId > 0)
            _touched.Add(productAttributeMappingId);
    }

    public async Task ClearTouchedAsync()
    {
        if (_touched.Count == 0)
            return;

        // By exact key: a prefix clear only reaches keys registered with that prefix.
        foreach (var mappingId in _touched)
            await staticCacheManager.RemoveAsync(NopCatalogDefaults.ProductAttributeValuesByAttributeCacheKey, mappingId);

        _touched.Clear();
    }
}
