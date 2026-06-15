using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
public interface IAkeneoAttributeMappingService
{
    Task InsertAkeneoAttributeMappingAsync(AkeneoAttributeMapping attributeMapping);
    Task UpdateAkeneoAttributeMappingAsync(AkeneoAttributeMapping attributeMapping);
    Task DeleteAkeneoAttributeMappingAsync(AkeneoAttributeMapping attributeMapping);

    Task<AkeneoAttributeMappingValidationResult> ValidateAttributeMappingsAsync(
        AkeneoAttributeMappingListModel model);

    Task SaveAttributeMappingsAsync(
        AkeneoAttributeMappingListModel model);

    Task<IList<AkeneoAttributeMapping>> GetAllAkeneoAttributeMappingsAsync();


}
