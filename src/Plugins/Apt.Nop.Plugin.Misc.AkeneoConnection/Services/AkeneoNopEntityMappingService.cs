using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
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


    public async Task UpsertAkeneoNopEntityMappingAsync(
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
            throw new ArgumentException(
                "Akeneo code or UUID is required for entity mapping.");
        }

        if (nopEntityId <= 0)
        {
            throw new ArgumentException(
                "nopCommerce entity ID must be greater than zero.",
                nameof(nopEntityId));
        }

        AkeneoNopEntityMapping mappingByUuid = null;
        AkeneoNopEntityMapping mappingByCode = null;

        if (!string.IsNullOrWhiteSpace(akeneoUuid))
        {
            mappingByUuid = await nopEntityMappingRepository.Table
                .FirstOrDefaultAsync(mapping =>
                    mapping.AkeneoEntityTypeId == (int)akeneoEntityType &&
                    mapping.NopEntityTypeId == (int)nopEntityType &&
                    mapping.AkeneoUuid == akeneoUuid);
        }

        if (!string.IsNullOrWhiteSpace(akeneoCode))
        {
            var normalizedAkeneoCode = akeneoCode.ToLowerInvariant();

            mappingByCode = await nopEntityMappingRepository.Table
                .FirstOrDefaultAsync(mapping =>
                    mapping.AkeneoEntityTypeId == (int)akeneoEntityType &&
                    mapping.NopEntityTypeId == (int)nopEntityType &&
                    mapping.AkeneoCode != null &&
                    mapping.AkeneoCode.ToLower() == normalizedAkeneoCode);
        }

        if (mappingByUuid != null &&
            mappingByCode != null &&
            mappingByUuid.Id != mappingByCode.Id)
        {
            throw new InvalidOperationException(
                $"Conflicting Akeneo mappings found. UUID '{akeneoUuid}' maps to mapping ID {mappingByUuid.Id}, " +
                $"but code '{akeneoCode}' maps to mapping ID {mappingByCode.Id}.");
        }

        var existingMapping = mappingByUuid ?? mappingByCode;

        if (existingMapping == null)
        {
            existingMapping = new AkeneoNopEntityMapping
            {
                AkeneoEntityTypeId = (int)akeneoEntityType,
                AkeneoCode = akeneoCode,
                AkeneoUuid = akeneoUuid,
                NopEntityTypeId = (int)nopEntityType,
                NopEntityId = nopEntityId,
                CreatedOnUtc = DateTime.UtcNow,
                UpdatedOnUtc = DateTime.UtcNow
            };

            await nopEntityMappingRepository.InsertAsync(existingMapping);
            return;
        }

        if (!string.IsNullOrWhiteSpace(akeneoCode))
            existingMapping.AkeneoCode = akeneoCode;

        if (!string.IsNullOrWhiteSpace(akeneoUuid))
            existingMapping.AkeneoUuid = akeneoUuid;

        existingMapping.NopEntityId = nopEntityId;
        existingMapping.UpdatedOnUtc = DateTime.UtcNow;

        await nopEntityMappingRepository.UpdateAsync(existingMapping);
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
}