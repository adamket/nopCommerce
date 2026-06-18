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
        string akeneoUuid,
        NopEntityType nopEntityType,
        int nopEntityId);

    Task DeleteAkeneoNopEntityMappingAsync(
        AkeneoEntityType akeneoEntityType,
        string akeneoCode,
        NopEntityType nopEntityType);


    Task<int?> GetMappedNopEntityIdByAkeneoUuidAsync(
        AkeneoEntityType akeneoEntityType,
        string akeneoUuid,
        NopEntityType nopEntityType);

    Task<int?> GetMappedNopEntityIdByAkeneoCodeAsync(
        AkeneoEntityType akeneoEntityType,
        string akeneoCode,
        NopEntityType nopEntityType);

    Task<AkeneoNopEntityMapping> GetAkeneoNopEntityMappingAsync(
        AkeneoEntityType akeneoEntityType,
        string akeneoCode,
        string akeneoUuid,
        NopEntityType nopEntityType);

    Task<int?> GetMappedNopEntityIdAsync(
        AkeneoEntityType akeneoEntityType,
        string akeneoCode,
        string akeneoUuid,
        NopEntityType nopEntityType);

    Task DeleteAkeneoNopEntityMappingByAkeneoUuidAsync(AkeneoEntityType akeneoEntityType,
        string akeneoUuid,
        NopEntityType nopEntityType);
}
