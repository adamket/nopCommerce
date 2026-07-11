using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Nop.Core.Caching;
using Nop.Data;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public class AkeneoAttributeMappingService(
    IRepository<AkeneoAttributeMapping> attributeMappingRepository,
    IStaticCacheManager staticCacheManager)
    : IAkeneoAttributeMappingService
{
    public async Task InsertAkeneoAttributeMappingAsync(AkeneoAttributeMapping attributeMapping)
    {
        await attributeMappingRepository.InsertAsync(attributeMapping);
        await ClearCacheAsync();
    }

    public async Task UpdateAkeneoAttributeMappingAsync(AkeneoAttributeMapping attributeMapping)
    {
        await attributeMappingRepository.UpdateAsync(attributeMapping);
        await ClearCacheAsync();
    }

    public async Task DeleteAkeneoAttributeMappingAsync(AkeneoAttributeMapping attributeMapping)
    {
        await attributeMappingRepository.DeleteAsync(attributeMapping);
        await ClearCacheAsync();
    }

    public async Task<IList<AkeneoAttributeMapping>> GetAllAkeneoAttributeMappingsAsync()
    {
        var cacheKey = staticCacheManager.PrepareKeyForDefaultCache(
            AkeneoConnectionConstants.AttributeMappingsAllCacheKey);

        return await staticCacheManager.GetAsync(cacheKey, async () =>
            await attributeMappingRepository.Table.ToListAsync());
    }

    public async Task<AkeneoAttributeMapping> GetAkeneoAttributeMappingByIdAsync(int id)
    {
        if (id <= 0)
            return null;

        var cacheKey = staticCacheManager.PrepareKeyForDefaultCache(
            AkeneoConnectionConstants.AttributeMappingsByIdCacheKey,
            id);

        return await staticCacheManager.GetAsync(cacheKey, async () =>
            await attributeMappingRepository.GetByIdAsync(id));
    }

    public async Task<AkeneoAttributeMapping> GetAkeneoAttributeMappingByCodeAsync(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;

        code = code.Trim();

        var cacheKey = staticCacheManager.PrepareKeyForDefaultCache(
            AkeneoConnectionConstants.AttributeMappingsByCodeCacheKey,
            code.ToLowerInvariant());

        return await staticCacheManager.GetAsync(cacheKey, async () =>
            await attributeMappingRepository.Table
                .FirstOrDefaultAsync(q => q.AkeneoAttributeCode == code));
    }

    public async Task ClearCacheAsync()
    {
        await staticCacheManager.RemoveByPrefixAsync(
            AkeneoConnectionConstants.AttributeMappingPrefix);
    }
}