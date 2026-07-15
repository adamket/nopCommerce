using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
public interface IAkeneoAttributeMappingService
{
    Task InsertAkeneoAttributeMappingAsync(AkeneoAttributeMapping attributeMapping);
    Task UpdateAkeneoAttributeMappingAsync(AkeneoAttributeMapping attributeMapping);
    Task DeleteAkeneoAttributeMappingAsync(AkeneoAttributeMapping attributeMapping);

    Task<IList<AkeneoAttributeMapping>> GetAllAkeneoAttributeMappingsAsync();

    Task<AkeneoAttributeMapping> GetAkeneoAttributeMappingByIdAsync(int id);

    Task<AkeneoAttributeMapping> GetAkeneoAttributeMappingByCodeAsync(string code, string familyCode = null);

    Task<IList<AkeneoAttributeMapping>>
        GetEffectiveMappingsAsync(string familyCode);

}
