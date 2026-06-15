using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
public interface IAkeneoNopEntityMappingService
{
    Task InsertAkeneoNopEntityMappingAsync(AkeneoNopEntityMapping entityMapping);
    Task UpdateAkeneoNopEntityMappingAsync(AkeneoNopEntityMapping entityMapping);
    Task DeleteAkeneoNopEntityMappingAsync(AkeneoNopEntityMapping entityMapping);

    Task<IList<AkeneoNopEntityMapping>> GetAkeneoNopEntityMappingsAsync(AkeneoEntityType? akeneoEntityType = null);

    Task UpsertAkeneoNopEntityMappingAsync(
        AkeneoEntityType akeneoEntityType,
        string akeneoCode,
        NopEntityType nopEntityType,
        int nopEntityId);

    Task DeleteAkeneoNopEntityMappingAsync(
        AkeneoEntityType akeneoEntityType,
        string akeneoCode,
        NopEntityType nopEntityType);
}
