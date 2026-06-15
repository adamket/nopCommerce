using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Nop.Data;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
public class AkeneoAttributeMappingService(IRepository<AkeneoAttributeMapping> attributeMappingRepository)
    : IAkeneoAttributeMappingService
{
    public async Task InsertAkeneoAttributeMappingAsync(AkeneoAttributeMapping attributeMapping)
    {
        await attributeMappingRepository.InsertAsync(attributeMapping);
    }

    public async Task UpdateAkeneoAttributeMappingAsync(AkeneoAttributeMapping attributeMapping)
    {
        await attributeMappingRepository.UpdateAsync(attributeMapping);
    }

    public async Task DeleteAkeneoAttributeMappingAsync(AkeneoAttributeMapping attributeMapping)
    {
        await attributeMappingRepository.DeleteAsync(attributeMapping);
    }

    public Task<AkeneoAttributeMappingValidationResult> ValidateAttributeMappingsAsync(AkeneoAttributeMappingListModel model)
    {
        throw new NotImplementedException();
    }

    public Task SaveAttributeMappingsAsync(AkeneoAttributeMappingListModel model)
    {
        throw new NotImplementedException();
    }

    public async Task<IList<AkeneoAttributeMapping>> GetAllAkeneoAttributeMappingsAsync()
    {
        return await attributeMappingRepository.Table.ToListAsync(); //TODO
    }
}