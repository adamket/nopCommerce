using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;
using Nop.Data;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public class AkeneoNopEntityMappingService(
    IRepository<AkeneoNopEntityMapping> nopEntityMappingRepository)
    : IAkeneoNopEntityMappingService
{
    public async Task InsertAkeneoNopEntityMappingAsync(
        AkeneoNopEntityMapping entityMapping)
    {
        await nopEntityMappingRepository.InsertAsync(entityMapping);
    }

    public async Task UpdateAkeneoNopEntityMappingAsync(
        AkeneoNopEntityMapping entityMapping)
    {
        await nopEntityMappingRepository.UpdateAsync(entityMapping);
    }

    public async Task DeleteAkeneoNopEntityMappingAsync(
        AkeneoNopEntityMapping entityMapping)
    {
        await nopEntityMappingRepository.DeleteAsync(entityMapping);
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
    }

    public async Task<AkeneoNopEntityMapping> GetAkeneoNopEntityMappingByAkeneoUuidAsync(AkeneoEntityType akeneoEntityType, string akeneoUuid,
        NopEntityType nopEntityType)
    {
        var item = await nopEntityMappingRepository.Table.FirstOrDefaultAsync(q=>q.AkeneoUuid == akeneoUuid 
                                                                      && q.AkeneoEntityTypeId == (int)akeneoEntityType 
                                                                      && q.NopEntityTypeId == (int)nopEntityType);

        return item;
    }

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

    public async Task<AkeneoNopEntityMapping> GetAkeneoNopEntityMappingAsync(
        AkeneoEntityType akeneoEntityType,
        string akeneoCode,
        string akeneoUuid,
        NopEntityType nopEntityType)
    {
        akeneoCode = akeneoCode?.Trim();
        akeneoUuid = akeneoUuid?.Trim();

        if (string.IsNullOrWhiteSpace(akeneoCode) &&
            string.IsNullOrWhiteSpace(akeneoUuid))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(akeneoUuid))
        {
            var mappingByUuid = await nopEntityMappingRepository.Table
                .FirstOrDefaultAsync(mapping =>
                    mapping.AkeneoEntityTypeId == (int)akeneoEntityType &&
                    mapping.NopEntityTypeId == (int)nopEntityType &&
                    mapping.AkeneoUuid == akeneoUuid);

            if (mappingByUuid != null)
                return mappingByUuid;
        }

        if (!string.IsNullOrWhiteSpace(akeneoCode))
        {
            var normalizedAkeneoCode = akeneoCode.ToLowerInvariant();

            var mappingByCode = await nopEntityMappingRepository.Table
                .FirstOrDefaultAsync(mapping =>
                    mapping.AkeneoEntityTypeId == (int)akeneoEntityType &&
                    mapping.NopEntityTypeId == (int)nopEntityType &&
                    mapping.AkeneoCode != null &&
                    mapping.AkeneoCode.ToLower() == normalizedAkeneoCode);

            if (mappingByCode != null)
                return mappingByCode;
        }

        return null;
    }

    public async Task<int?> GetMappedNopEntityIdAsync(
        AkeneoEntityType akeneoEntityType,
        string akeneoCode,
        string akeneoUuid,
        NopEntityType nopEntityType)
    {
        var mapping = await GetAkeneoNopEntityMappingAsync(
            akeneoEntityType,
            akeneoCode,
            akeneoUuid,
            nopEntityType);

        if (mapping == null || mapping.NopEntityId <= 0)
            return null;

        return mapping.NopEntityId;
    }

    public async Task<int?> GetMappedNopEntityIdByAkeneoUuidAsync(
        AkeneoEntityType akeneoEntityType,
        string akeneoUuid,
        NopEntityType nopEntityType)
    {
        return await GetMappedNopEntityIdAsync(
            akeneoEntityType,
            null,
            akeneoUuid,
            nopEntityType);
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


    public async Task<int?> GetMappedNopEntityIdByAkeneoCodeAsync(
        AkeneoEntityType akeneoEntityType,
        string akeneoCode,
        NopEntityType nopEntityType)
    {
        return await GetMappedNopEntityIdAsync(
            akeneoEntityType,
            akeneoCode,
            null,
            nopEntityType);
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
}