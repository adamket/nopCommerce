using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public interface IAkeneoAssetMappingService
{
    Task<IList<AkeneoAssetMapping>> GetAllAsync();
    Task<AkeneoAssetMapping> GetByIdAsync(int id);
    Task<IList<AkeneoAssetMapping>> GetEffectiveMappingsAsync(string familyCode);
    Task InsertAsync(AkeneoAssetMapping mapping);
    Task UpdateAsync(AkeneoAssetMapping mapping);
    Task DeleteAsync(AkeneoAssetMapping mapping);
    Task ClearCacheAsync();
}
