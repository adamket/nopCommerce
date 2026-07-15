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

    public async Task<AkeneoAttributeMapping>
        GetAkeneoAttributeMappingByCodeAsync(
            string attributeCode,
            string familyCode = null)
    {
        if (string.IsNullOrWhiteSpace(attributeCode))
            return null;

        attributeCode = attributeCode.Trim();
        familyCode = string.IsNullOrWhiteSpace(familyCode)
            ? null
            : familyCode.Trim();

        var cacheKey = staticCacheManager.PrepareKeyForDefaultCache(
            AkeneoConnectionConstants.AttributeMappingsByCodeCacheKey,
            familyCode?.ToLowerInvariant() ?? "global",
            attributeCode.ToLowerInvariant());

        return await staticCacheManager.GetAsync(
            cacheKey,
            async () => await attributeMappingRepository.Table
                .FirstOrDefaultAsync(mapping =>
                    mapping.AkeneoAttributeCode == attributeCode &&
                    mapping.AkeneoFamilyCode == familyCode));
    }

    public async Task<IList<AkeneoAttributeMapping>>
        GetEffectiveMappingsAsync(string familyCode)
    {
        var mappings = await GetAllAkeneoAttributeMappingsAsync();

        familyCode = string.IsNullOrWhiteSpace(familyCode)
            ? null
            : familyCode.Trim();

        var effectiveMappings = mappings
            .Where(mapping =>
                string.IsNullOrWhiteSpace(mapping.AkeneoFamilyCode))
            .ToDictionary(
                mapping => mapping.AkeneoAttributeCode,
                mapping => mapping,
                StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(familyCode))
        {
            var familyMappings = mappings.Where(mapping =>
                string.Equals(
                    mapping.AkeneoFamilyCode,
                    familyCode,
                    StringComparison.OrdinalIgnoreCase));

            foreach (var familyMapping in familyMappings)
            {
                // Family mapping replaces global mapping, including an
                // explicit Ignore mapping.
                effectiveMappings[familyMapping.AkeneoAttributeCode] =
                    familyMapping;
            }
        }

        return effectiveMappings.Values
            .OrderBy(mapping => mapping.AkeneoAttributeCode)
            .ToList();
    }

    public async Task ClearCacheAsync()
    {
        await staticCacheManager.RemoveByPrefixAsync(
            AkeneoConnectionConstants.AttributeMappingPrefix);
    }
}