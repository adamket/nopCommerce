using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
public interface IAkeneoAttributeMappingService
{
    Task InsertAkeneoAttributeMappingAsync(AkeneoAttributeMapping attributeMapping);
    Task UpdateAkeneoAttributeMappingAsync(AkeneoAttributeMapping attributeMapping);
    Task DeleteAkeneoAttributeMappingAsync(AkeneoAttributeMapping attributeMapping);
}
