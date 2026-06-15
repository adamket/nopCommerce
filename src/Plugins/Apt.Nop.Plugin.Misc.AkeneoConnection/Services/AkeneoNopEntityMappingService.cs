using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Nop.Data;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
public class AkeneoNopEntityMappingService(IRepository<AkeneoNopEntityMapping> nopEntityMappingRepository)
    : IAkeneoNopEntityMappingService
{

    public async Task InsertAkeneoNopEntityMappingAsync(AkeneoNopEntityMapping entityMapping)
    {
        await nopEntityMappingRepository.InsertAsync(entityMapping);
    }

    public async Task UpdateAkeneoNopEntityMappingAsync(AkeneoNopEntityMapping entityMapping)
    {
        await nopEntityMappingRepository.UpdateAsync(entityMapping);
    }

    public async Task DeleteAkeneoNopEntityMappingAsync(AkeneoNopEntityMapping entityMapping)
    {
        await nopEntityMappingRepository.DeleteAsync(entityMapping);
    }

    public async Task<IList<AkeneoNopEntityMapping>> GetAkeneoNopEntityMappingsAsync(AkeneoEntityType? akeneoEntityType = null)
    {
        var query = nopEntityMappingRepository.Table;

        if (akeneoEntityType.HasValue)
        {
            query = query.Where(q => q.AkeneoEntityTypeId == (int)akeneoEntityType.Value);
        }

        return await query.ToListAsync();
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

        var existingMappings = await nopEntityMappingRepository.GetAllAsync(query =>
            query.Where(mapping =>
                mapping.AkeneoEntityTypeId == (int)akeneoEntityType &&
                mapping.AkeneoCode == akeneoCode &&
                mapping.NopEntityTypeId == (int)nopEntityType));

        var existingMapping = existingMappings.FirstOrDefault();

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

        var mappings = await nopEntityMappingRepository.GetAllAsync(query =>
            query.Where(mapping =>
                mapping.AkeneoEntityTypeId == (int)akeneoEntityType &&
                mapping.AkeneoCode == akeneoCode &&
                mapping.NopEntityTypeId == (int)nopEntityType));

        await nopEntityMappingRepository.DeleteAsync(mappings);
    }


}
