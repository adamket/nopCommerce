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

    public async Task<AkeneoAttributeMapping> GetAkeneoAttributeMappingByCodeAsync(
        string attributeCode,
        string familyCode = null)
    {
        var mappings = await GetAkeneoAttributeMappingsByCodeAsync(
            attributeCode,
            familyCode);

        return mappings.FirstOrDefault();
    }

    public async Task<IList<AkeneoAttributeMapping>> GetAkeneoAttributeMappingsByCodeAsync(
        string attributeCode,
        string familyCode = null)
    {
        if (string.IsNullOrWhiteSpace(attributeCode))
            return new List<AkeneoAttributeMapping>();

        attributeCode = attributeCode.Trim();
        familyCode = NormalizeScope(familyCode);

        var cacheKey = staticCacheManager.PrepareKeyForDefaultCache(
            AkeneoConnectionConstants.AttributeMappingsBySourceCacheKey,
            familyCode?.ToLowerInvariant() ?? "global",
            attributeCode.ToLowerInvariant());

        return await staticCacheManager.GetAsync(
            cacheKey,
            async () =>
            {
                var query = attributeMappingRepository.Table.Where(mapping =>
                    mapping.AkeneoAttributeCode == attributeCode);

                query = familyCode == null
                    ? query.Where(mapping =>
                        mapping.AkeneoFamilyCode == null ||
                        mapping.AkeneoFamilyCode == string.Empty)
                    : query.Where(mapping =>
                        mapping.AkeneoFamilyCode == familyCode);

                return await query
                    .OrderBy(mapping => mapping.Id)
                    .ToListAsync();
            });
    }

    public async Task<IList<AkeneoAttributeMapping>> GetEffectiveMappingsAsync(
        string familyCode)
    {
        var mappings = await GetAllAkeneoAttributeMappingsAsync();
        familyCode = NormalizeScope(familyCode);

        var effectiveMappings = new Dictionary<string, AkeneoAttributeMapping>(
            StringComparer.OrdinalIgnoreCase);

        // Global rows establish the defaults. For a reference-entity attribute,
        // each selected reference field is an independent mapping slot.
        foreach (var mapping in mappings
                     .Where(mapping => string.IsNullOrWhiteSpace(mapping.AkeneoFamilyCode))
                     .OrderBy(mapping => mapping.Id))
        {
            effectiveMappings[GetMappingSlotKey(mapping)] = mapping;
        }

        // A family row replaces only the matching slot. This preserves the
        // remaining inherited reference-entity field mappings.
        if (!string.IsNullOrWhiteSpace(familyCode))
        {
            foreach (var mapping in mappings
                         .Where(mapping => string.Equals(
                             mapping.AkeneoFamilyCode,
                             familyCode,
                             StringComparison.OrdinalIgnoreCase))
                         .OrderBy(mapping => mapping.Id))
            {
                effectiveMappings[GetMappingSlotKey(mapping)] = mapping;
            }
        }

        return effectiveMappings.Values
            .OrderBy(mapping => mapping.AkeneoAttributeCode)
            .ThenBy(mapping => mapping.AkeneoReferenceEntityAttributeCode)
            .ThenBy(mapping => mapping.Id)
            .ToList();
    }

    public async Task ClearCacheAsync()
    {
        await staticCacheManager.RemoveByPrefixAsync(
            AkeneoConnectionConstants.AttributeMappingPrefix);
    }

    private static string GetMappingSlotKey(AkeneoAttributeMapping mapping)
    {
        var attributeCode = NormalizeKeyPart(mapping.AkeneoAttributeCode);

        if (!IsReferenceEntityType(mapping.AkeneoAttributeTypeId))
            return attributeCode;

        var referenceField = NormalizeKeyPart(
            mapping.AkeneoReferenceEntityAttributeCode);

        return $"{attributeCode}\u001f{referenceField}";
    }

    private static bool IsReferenceEntityType(int attributeTypeId)
    {
        return attributeTypeId == (int)AkeneoAttributeType.ReferenceEntity ||
               attributeTypeId == (int)AkeneoAttributeType.ReferenceEntityCollection;
    }

    private static string NormalizeScope(string familyCode)
    {
        return string.IsNullOrWhiteSpace(familyCode)
            ? null
            : familyCode.Trim();
    }

    private static string NormalizeKeyPart(string value)
    {
        return value?.Trim().ToLowerInvariant() ?? string.Empty;
    }
}
