using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Nop.Core.Caching;
using Nop.Data;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public sealed class AkeneoAssetMappingService(
    IRepository<AkeneoAssetMapping> repository,
    IStaticCacheManager cacheManager)
    : IAkeneoAssetMappingService
{
    public async Task<IList<AkeneoAssetMapping>> GetAllAsync()
    {
        var key = cacheManager.PrepareKeyForDefaultCache(
            AkeneoConnectionConstants.AssetMappingsAllCacheKey);

        return await cacheManager.GetAsync(key, async () =>
            await repository.Table
                .OrderBy(mapping => mapping.DisplayOrder)
                .ThenBy(mapping => mapping.Name)
                .ThenBy(mapping => mapping.Id)
                .ToListAsync());
    }

    public async Task<AkeneoAssetMapping> GetByIdAsync(int id)
    {
        if (id <= 0)
            return null;

        var key = cacheManager.PrepareKeyForDefaultCache(
            AkeneoConnectionConstants.AssetMappingsByIdCacheKey,
            id);

        return await cacheManager.GetAsync(key, async () =>
            await repository.GetByIdAsync(id));
    }

    public async Task<IList<AkeneoAssetMapping>> GetEffectiveMappingsAsync(
        string familyCode)
    {
        var all = await GetAllAsync();
        var normalizedFamily = Normalize(familyCode);
        var effective = new Dictionary<string, AkeneoAssetMapping>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var mapping in all
                     .Where(item => string.IsNullOrWhiteSpace(item.AkeneoFamilyCode))
                     .OrderBy(item => item.DisplayOrder)
                     .ThenBy(item => item.Id))
        {
            effective[GetSlotKey(mapping)] = mapping;
        }

        if (normalizedFamily != null)
        {
            foreach (var mapping in all
                         .Where(item => string.Equals(
                             Normalize(item.AkeneoFamilyCode),
                             normalizedFamily,
                             StringComparison.OrdinalIgnoreCase))
                         .OrderBy(item => item.DisplayOrder)
                         .ThenBy(item => item.Id))
            {
                effective[GetSlotKey(mapping)] = mapping;
            }
        }

        return effective.Values
            .OrderBy(item => item.DisplayOrder)
            .ThenBy(item => item.Name)
            .ThenBy(item => item.Id)
            .ToList();
    }

    public async Task InsertAsync(AkeneoAssetMapping mapping)
    {
        await repository.InsertAsync(mapping);
        await ClearCacheAsync();
    }

    public async Task UpdateAsync(AkeneoAssetMapping mapping)
    {
        await repository.UpdateAsync(mapping);
        await ClearCacheAsync();
    }

    public async Task DeleteAsync(AkeneoAssetMapping mapping)
    {
        await repository.DeleteAsync(mapping);
        await ClearCacheAsync();
    }

    public async Task ClearCacheAsync()
    {
        await cacheManager.RemoveByPrefixAsync(
            AkeneoConnectionConstants.AssetMappingPrefix);
    }

    private static string GetSlotKey(AkeneoAssetMapping mapping)
    {
        return string.IsNullOrWhiteSpace(mapping.MappingKey)
            ? $"legacy-{mapping.Id}"
            : mapping.MappingKey.Trim();
    }

    private static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
