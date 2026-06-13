using Apt.Nop.Plugin.AkeneoConnection.Domain;

namespace Apt.Nop.Plugin.AkeneoConnection.Services;
public interface IAkeneoNopEntityMappingService
{
    Task InsertAkeneoNopEntityMappingAsync(AkeneoNopEntityMapping entityMapping);
    Task UpdateAkeneoNopEntityMappingAsync(AkeneoNopEntityMapping entityMapping);
    Task DeleteAkeneoNopEntityMappingAsync(AkeneoNopEntityMapping entityMapping);
}
