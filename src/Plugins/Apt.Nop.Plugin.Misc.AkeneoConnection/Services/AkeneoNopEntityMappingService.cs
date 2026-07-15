using Apt.Nop.Plugin.Misc.AkeneoConnection.Data;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;
using Nop.Core.Caching;
using Nop.Data;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public class AkeneoNopEntityMappingService(
    IRepository<AkeneoNopEntityMapping> nopEntityMappingRepository,
    IStaticCacheManager staticCacheManager)
    : IAkeneoNopEntityMappingService
{
    #region Caching

    private static bool IsCacheableType(AkeneoEntityType akeneoEntityType) =>
        akeneoEntityType is not (AkeneoEntityType.Product or AkeneoEntityType.ProductModel);

    private async Task InvalidateAkeneoEntityTypeCacheAsync(int akeneoEntityTypeId)
    {
        if (!IsCacheableType((AkeneoEntityType)akeneoEntityTypeId))
            return;

        await staticCacheManager.RemoveByPrefixAsync(
            AkeneoConnectionConstants.EntityMappingByAkeneoTypePrefix, akeneoEntityTypeId);
    }

    #endregion

    public async Task InsertAkeneoNopEntityMappingAsync(
        AkeneoNopEntityMapping entityMapping)
    {
        await nopEntityMappingRepository.InsertAsync(entityMapping);
        await InvalidateAkeneoEntityTypeCacheAsync(entityMapping.AkeneoEntityTypeId);
    }

    public async Task UpdateAkeneoNopEntityMappingAsync(
        AkeneoNopEntityMapping entityMapping)
    {
        await nopEntityMappingRepository.UpdateAsync(entityMapping);
        await InvalidateAkeneoEntityTypeCacheAsync(entityMapping.AkeneoEntityTypeId);
    }

    public async Task DeleteAkeneoNopEntityMappingAsync(
        AkeneoNopEntityMapping entityMapping)
    {
        await nopEntityMappingRepository.DeleteAsync(entityMapping);
        await InvalidateAkeneoEntityTypeCacheAsync(entityMapping.AkeneoEntityTypeId);
    }

    public async Task DeleteAkeneoNopEntityMappingByAkeneoUuidAsync(
        AkeneoEntityType akeneoEntityType,
        string akeneoUuid,
        NopEntityType nopEntityType)
    {
        if (string.IsNullOrWhiteSpace(akeneoUuid))
            return;

        akeneoUuid = akeneoUuid.Trim();

        var mappings = await nopEntityMappingRepository.GetAllAsync(query =>
            query.Where(mapping =>
                mapping.AkeneoEntityTypeId == (int)akeneoEntityType &&
                mapping.NopEntityTypeId == (int)nopEntityType &&
                mapping.AkeneoUuid == akeneoUuid));

        if (!mappings.Any())
            return;

        await nopEntityMappingRepository.DeleteAsync(mappings);
        await InvalidateAkeneoEntityTypeCacheAsync((int)akeneoEntityType);
    }

    // Write-path existence check — intentionally NOT cached so upsert decisions read live DB state.
    public async Task<AkeneoNopEntityMapping> GetAkeneoNopEntityMappingByAkeneoUuidAsync(AkeneoEntityType akeneoEntityType, string akeneoUuid,
        NopEntityType nopEntityType)
    {
        var item = await nopEntityMappingRepository.Table.FirstOrDefaultAsync(q => q.AkeneoUuid == akeneoUuid
                                                                      && q.AkeneoEntityTypeId == (int)akeneoEntityType
                                                                      && q.NopEntityTypeId == (int)nopEntityType);

        return item;
    }

    // Write-path existence check — intentionally NOT cached.
    public async Task<AkeneoNopEntityMapping> GetAkeneoNopEntityMappingByAkeneoCodeAsync(AkeneoEntityType akeneoEntityType, string akeneoCode,
        NopEntityType nopEntityType)
    {
        var item = await nopEntityMappingRepository.Table.FirstOrDefaultAsync(q => q.AkeneoCode == akeneoCode
            && q.AkeneoEntityTypeId == (int)akeneoEntityType
            && q.NopEntityTypeId == (int)nopEntityType);

        return item;
    }

    public async Task<IList<AkeneoNopEntityMapping>> GetAkeneoNopEntityMappingsAsync(
        AkeneoEntityType? akeneoEntityType = null)
    {
        var query = nopEntityMappingRepository.Table;

        if (akeneoEntityType.HasValue)
        {
            query = query.Where(mapping =>
                mapping.AkeneoEntityTypeId == (int)akeneoEntityType.Value);
        }

        return await query.ToListAsync();
    }

    public async Task<bool> UpsertAkeneoNopEntityMappingAsync(
     AkeneoEntityType akeneoEntityType,
     string akeneoCode,
     string akeneoUuid,
     NopEntityType nopEntityType,
     int nopEntityId)
    {
        akeneoCode = akeneoCode?.Trim();
        akeneoUuid = akeneoUuid?.Trim();

        if (string.IsNullOrWhiteSpace(akeneoCode) &&
            string.IsNullOrWhiteSpace(akeneoUuid))
        {
            throw new ArgumentException("Either Akeneo code or Akeneo UUID must be provided.");
        }

        if (nopEntityId <= 0)
            throw new ArgumentException("nopCommerce entity ID must be greater than zero.", nameof(nopEntityId));

        var akeneoEntityTypeId = (int)akeneoEntityType;
        var nopEntityTypeId = (int)nopEntityType;

        var mapping = await FindExistingMappingAsync(
            akeneoEntityType,
            akeneoCode,
            akeneoUuid,
            nopEntityType);

        // Insert/Update below handle cache invalidation via their own calls.
        if (mapping == null)
        {
            mapping = new AkeneoNopEntityMapping
            {
                AkeneoEntityTypeId = akeneoEntityTypeId,
                AkeneoCode = akeneoCode,
                AkeneoUuid = akeneoUuid,
                NopEntityTypeId = nopEntityTypeId,
                NopEntityId = nopEntityId,
                CreatedOnUtc = DateTime.UtcNow,
                UpdatedOnUtc = DateTime.UtcNow
            };

            await InsertAkeneoNopEntityMappingAsync(mapping);

            return true;
        }

        var changed = false;

        changed |= AkeneoMappingHelper.SetIfChanged(
            mapping.AkeneoEntityTypeId,
            akeneoEntityTypeId,
            value => mapping.AkeneoEntityTypeId = value);

        changed |= AkeneoMappingHelper.SetIfChanged(
            mapping.AkeneoCode,
            akeneoCode,
            value => mapping.AkeneoCode = value);

        changed |= AkeneoMappingHelper.SetIfChanged(
            mapping.AkeneoUuid,
            akeneoUuid,
            value => mapping.AkeneoUuid = value);

        changed |= AkeneoMappingHelper.SetIfChanged(
            mapping.NopEntityTypeId,
            nopEntityTypeId,
            value => mapping.NopEntityTypeId = value);

        changed |= AkeneoMappingHelper.SetIfChanged(
            mapping.NopEntityId,
            nopEntityId,
            value => mapping.NopEntityId = value);

        if (!changed)
            return false;

        mapping.UpdatedOnUtc = DateTime.UtcNow;

        await UpdateAkeneoNopEntityMappingAsync(mapping);

        return true;
    }

    // UUID read path — NOT cached: only Product carries a UUID and Product is excluded by the
    // cacheable guard, so a cache here would never be populated. Left as a live read.
    public async Task<AkeneoNopEntityMapping> GetMappedNopEntityByAkeneoUuidAsync(
        AkeneoEntityType akeneoEntityType,
        string akeneoUuid,
        NopEntityType nopEntityType)
    {
        var mappingByUuid = await nopEntityMappingRepository.Table
            .FirstOrDefaultAsync(mapping =>
                mapping.AkeneoEntityTypeId == (int)akeneoEntityType &&
                mapping.NopEntityTypeId == (int)nopEntityType &&
                mapping.AkeneoUuid == akeneoUuid);

        return mappingByUuid;
    }

    public async Task<int?> GetMappedNopEntityIdByAkeneoUuidAsync(
        AkeneoEntityType akeneoEntityType,
        string akeneoUuid,
        NopEntityType nopEntityType)
    {
        var mappingByUuid = await GetMappedNopEntityByAkeneoUuidAsync(akeneoEntityType, akeneoUuid, nopEntityType);

        return mappingByUuid?.NopEntityId; 
    }

    // Read hot path — cached for config/reference types, keyed by (akeneoType, nopType, code).
    // The Id method below rides on this cache automatically.
    public async Task<AkeneoNopEntityMapping> GetMappedNopEntityByAkeneoCodeAsync(AkeneoEntityType akeneoEntityType,
        string akeneoCode,
        NopEntityType nopEntityType)
    {
        var akeneoEntityTypeId = (int)akeneoEntityType;
        var nopEntityTypeId = (int)nopEntityType;

        Task<AkeneoNopEntityMapping> AcquireAsync() =>
            nopEntityMappingRepository.Table.FirstOrDefaultAsync(mapping =>
                mapping.AkeneoEntityTypeId == akeneoEntityTypeId &&
                mapping.NopEntityTypeId == nopEntityTypeId &&
                mapping.AkeneoCode == akeneoCode);

        if (!IsCacheableType(akeneoEntityType))
            return await AcquireAsync();

        var cacheKey = staticCacheManager.PrepareKeyForDefaultCache(
            AkeneoConnectionConstants.EntityMappingByCodeCacheKey,
            akeneoEntityTypeId, nopEntityTypeId, akeneoCode);

        return await staticCacheManager.GetAsync(cacheKey, AcquireAsync);
    }

    public async Task<int?> GetMappedNopEntityIdByAkeneoCodeAsync(
        AkeneoEntityType akeneoEntityType,
        string akeneoCode,
        NopEntityType nopEntityType)
    {
        var mappingByCode = await GetMappedNopEntityByAkeneoCodeAsync(akeneoEntityType, akeneoCode, nopEntityType);
        return mappingByCode?.NopEntityId; 
    }

    public async Task DeleteAkeneoNopEntityMappingAsync(
        AkeneoEntityType akeneoEntityType,
        string akeneoCode,
        NopEntityType nopEntityType)
    {
        if (string.IsNullOrWhiteSpace(akeneoCode))
            return;

        akeneoCode = akeneoCode.Trim();
        var normalizedAkeneoCode = akeneoCode.ToLowerInvariant();

        var mappings = await nopEntityMappingRepository.GetAllAsync(query =>
            query.Where(mapping =>
                mapping.AkeneoEntityTypeId == (int)akeneoEntityType &&
                mapping.NopEntityTypeId == (int)nopEntityType &&
                mapping.AkeneoCode != null &&
                mapping.AkeneoCode.ToLower() == normalizedAkeneoCode));

        if (!mappings.Any())
            return;

        await nopEntityMappingRepository.DeleteAsync(mappings);
        await InvalidateAkeneoEntityTypeCacheAsync((int)akeneoEntityType);
    }

    private async Task<AkeneoNopEntityMapping> FindExistingMappingAsync(
        AkeneoEntityType akeneoEntityType,
        string akeneoCode,
        string akeneoUuid,
        NopEntityType nopEntityType)
    {
        if (!string.IsNullOrWhiteSpace(akeneoUuid))
        {
            var mappingByUuid = await GetAkeneoNopEntityMappingByAkeneoUuidAsync(
                akeneoEntityType,
                akeneoUuid,
                nopEntityType);

            if (mappingByUuid != null)
                return mappingByUuid;
        }

        if (!string.IsNullOrWhiteSpace(akeneoCode))
        {
            var mappingByCode = await GetAkeneoNopEntityMappingByAkeneoCodeAsync(
                akeneoEntityType,
                akeneoCode,
                nopEntityType);

            if (mappingByCode != null)
                return mappingByCode;
        }

        return null;
    }

    public async Task<IList<AkeneoNopEntityMapping>>
        GetMappingsByNopEntityAsync(
            NopEntityType nopEntityType,
            int nopEntityId)
    {
        return await nopEntityMappingRepository.Table
            .Where(mapping =>
                mapping.NopEntityTypeId == (int)nopEntityType &&
                mapping.NopEntityId == nopEntityId)
            .ToListAsync();
    }

    public async Task DeleteMappingsByNopEntityAsync(
        NopEntityType nopEntityType,
        int nopEntityId)
    {
        var mappings = await GetMappingsByNopEntityAsync(
            nopEntityType,
            nopEntityId);

        if (!mappings.Any())
            return;

        await nopEntityMappingRepository.DeleteAsync(mappings);

        foreach (var entityTypeId in mappings
                     .Select(mapping => mapping.AkeneoEntityTypeId)
                     .Distinct())
        {
            await InvalidateAkeneoEntityTypeCacheAsync(
                entityTypeId);
        }
    }
}