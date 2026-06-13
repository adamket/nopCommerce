using Apt.Nop.Plugin.AkeneoConnection.Domain;

namespace Apt.Nop.Plugin.AkeneoConnection.Services;
public interface IAkeneoAttributeMappingService
{
    Task InsertAkeneoAttributeMappingAsync(AkeneoAttributeMapping attributeMapping);
    Task UpdateAkeneoAttributeMappingAsync(AkeneoAttributeMapping attributeMapping);
    Task DeleteAkeneoAttributeMappingAsync(AkeneoAttributeMapping attributeMapping);
}
