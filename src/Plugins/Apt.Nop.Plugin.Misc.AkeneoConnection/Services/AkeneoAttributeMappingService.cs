using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Nop.Data;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
public class AkeneoAttributeMappingService(IRepository<AkeneoAttributeMapping> akeneoAttributeMappingRepository)
    : IAkeneoAttributeMappingService
{
    public async Task InsertAkeneoAttributeMappingAsync(AkeneoAttributeMapping attributeMapping)
    {
        await akeneoAttributeMappingRepository.InsertAsync(attributeMapping);
    }

    public async Task UpdateAkeneoAttributeMappingAsync(AkeneoAttributeMapping attributeMapping)
    {
        await akeneoAttributeMappingRepository.UpdateAsync(attributeMapping);
    }

    public async Task DeleteAkeneoAttributeMappingAsync(AkeneoAttributeMapping attributeMapping)
    {
        await akeneoAttributeMappingRepository.DeleteAsync(attributeMapping);
    }
}