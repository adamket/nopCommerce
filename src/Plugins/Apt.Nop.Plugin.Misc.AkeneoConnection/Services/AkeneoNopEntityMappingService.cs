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
        NopEntityType nopEntityType)
    {
        if (string.IsNullOrWhiteSpace(akeneoCode))
            return null;

        akeneoCode = akeneoCode.Trim();
        var normalizedAkeneoCode = akeneoCode.ToLowerInvariant();

        var mappings = await nopEntityMappingRepository.GetAllAsync(query =>
            query.Where(mapping =>
                mapping.AkeneoEntityTypeId == (int)akeneoEntityType &&
                mapping.NopEntityTypeId == (int)nopEntityType &&
                mapping.AkeneoCode != null &&
                mapping.AkeneoCode.ToLower() == normalizedAkeneoCode));

        return mappings.FirstOrDefault();
    }

    public async Task<int?> GetMappedNopEntityIdAsync(
        AkeneoEntityType akeneoEntityType,
        string akeneoCode,
        NopEntityType nopEntityType)
    {
        var mapping = await GetAkeneoNopEntityMappingAsync(
            akeneoEntityType,
            akeneoCode,
            nopEntityType);

        if (mapping == null || mapping.NopEntityId <= 0)
            return null;

        return mapping.NopEntityId;
    }

    public async Task UpsertAkeneoNopEntityMappingAsync(
       AkeneoEntityType akeneoEntityType,
       string akeneoCode,
       NopEntityType nopEntityType,
       int nopEntityId)
    {
        if (string.IsNullOrWhiteSpace(akeneoCode))
            throw new ArgumentException("Akeneo code is required.", nameof(akeneoCode));

        if (nopEntityId <= 0)
            throw new ArgumentException("nopCommerce entity ID must be greater than zero.", nameof(nopEntityId));

        akeneoCode = akeneoCode.Trim();

        var existingMapping = await GetAkeneoNopEntityMappingAsync(
            akeneoEntityType,
            akeneoCode,
            nopEntityType);

        if (existingMapping == null)
        {
            existingMapping = new AkeneoNopEntityMapping
            {
                AkeneoEntityTypeId = (int)akeneoEntityType,
                AkeneoCode = akeneoCode,
                NopEntityTypeId = (int)nopEntityType,
                NopEntityId = nopEntityId
            };

            await nopEntityMappingRepository.InsertAsync(existingMapping);
            return;
        }

        if (existingMapping.NopEntityId == nopEntityId)
            return;

        existingMapping.NopEntityId = nopEntityId;

        await nopEntityMappingRepository.UpdateAsync(existingMapping);
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