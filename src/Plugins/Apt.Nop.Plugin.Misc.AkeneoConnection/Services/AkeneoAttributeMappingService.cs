using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Nop.Core.Caching;
using Nop.Data;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public class AkeneoAttributeMappingService(
    IRepository<AkeneoAttributeMapping> attributeMappingRepository,
    IRepository<AkeneoAttributeMappingFallbackSource> fallbackSourceRepository,
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
        var fallbackSources = await fallbackSourceRepository.Table
            .Where(source => source.AttributeMappingId == attributeMapping.Id)
            .ToListAsync();

        foreach (var fallbackSource in fallbackSources)
            await fallbackSourceRepository.DeleteAsync(fallbackSource);

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
                    mapping.ValueModeId ==
                        (int)AkeneoAttributeMappingValueMode.SingleAttribute &&
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
            .OrderBy(mapping => mapping.ValueModeId)
            .ThenBy(mapping => mapping.Name)
            .ThenBy(mapping => mapping.AkeneoAttributeCode)
            .ThenBy(mapping => mapping.AkeneoReferenceEntityAttributeCode)
            .ThenBy(mapping => mapping.Id)
            .ToList();
    }

    public async Task<IList<AkeneoAttributeMappingFallbackSource>>
        GetAllFallbackSourcesAsync()
    {
        var cacheKey = staticCacheManager.PrepareKeyForDefaultCache(
            AkeneoConnectionConstants.AttributeMappingFallbackSourcesAllCacheKey);

        return await staticCacheManager.GetAsync(
            cacheKey,
            async () => await fallbackSourceRepository.Table
                .OrderBy(source => source.AttributeMappingId)
                .ThenBy(source => source.DisplayOrder)
                .ThenBy(source => source.Id)
                .ToListAsync());
    }

    public async Task ReplaceFallbackSourcesAsync(
        int attributeMappingId,
        IEnumerable<AkeneoAttributeMappingFallbackSource> fallbackSources)
    {
        if (attributeMappingId <= 0)
            throw new ArgumentOutOfRangeException(nameof(attributeMappingId));

        var existing = await fallbackSourceRepository.Table
            .Where(source => source.AttributeMappingId == attributeMappingId)
            .ToListAsync();

        foreach (var source in existing)
            await fallbackSourceRepository.DeleteAsync(source);

        var normalized = (fallbackSources ??
                Enumerable.Empty<AkeneoAttributeMappingFallbackSource>())
            .Where(source =>
                !string.IsNullOrWhiteSpace(source.AkeneoAttributeCode))
            .Select((source, index) =>
                new AkeneoAttributeMappingFallbackSource
                {
                    AttributeMappingId = attributeMappingId,
                    AkeneoAttributeCode =
                        source.AkeneoAttributeCode.Trim(),
                    AkeneoAttributeTypeId =
                        source.AkeneoAttributeTypeId,
                    AkeneoReferenceEntityCode = NormalizeScope(
                        source.AkeneoReferenceEntityCode),
                    AkeneoReferenceEntityAttributeCode = NormalizeScope(
                        source.AkeneoReferenceEntityAttributeCode),
                    DisplayOrder = index
                })
            .ToList();

        foreach (var source in normalized)
            await fallbackSourceRepository.InsertAsync(source);

        await ClearCacheAsync();
    }

    public async Task ClearCacheAsync()
    {
        await staticCacheManager.RemoveByPrefixAsync(
            AkeneoConnectionConstants.AttributeMappingPrefix);
    }

    private static string GetMappingSlotKey(AkeneoAttributeMapping mapping)
    {
        if (mapping.ValueModeId ==
            (int)AkeneoAttributeMappingValueMode.Template)
        {
            var mappingKey = NormalizeKeyPart(mapping.MappingKey);

            // MappingKey is required for new computed mappings. The id fallback
            // keeps malformed legacy rows independent rather than collapsing
            // them into one slot.
            return string.IsNullOrWhiteSpace(mappingKey)
                ? $"template\u001flegacy-{mapping.Id}"
                : $"template\u001f{mappingKey}";
        }

        var attributeCode = NormalizeKeyPart(mapping.AkeneoAttributeCode);

        if (!IsReferenceEntityType(mapping.AkeneoAttributeTypeId))
            return $"attribute\u001f{attributeCode}";

        var referenceField = NormalizeKeyPart(
            mapping.AkeneoReferenceEntityAttributeCode);

        return $"attribute\u001f{attributeCode}\u001f{referenceField}";
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
