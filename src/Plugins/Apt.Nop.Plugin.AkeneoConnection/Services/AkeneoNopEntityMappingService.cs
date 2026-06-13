using Apt.Nop.Plugin.AkeneoConnection.Domain;
using Nop.Data;

namespace Apt.Nop.Plugin.AkeneoConnection.Services;
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
}
