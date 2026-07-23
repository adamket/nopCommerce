using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public interface IAkeneoManagedAssetService
{
    Task<IList<AkeneoManagedAsset>> GetByProductAndMappingAsync(
        int nopProductId,
        string assetMappingKey);

    Task<AkeneoManagedAsset> GetByIdentityAsync(
        int nopProductId,
        string assetMappingKey,
        string sourceIdentityHash);

    Task<IList<AkeneoManagedAsset>> GetByProductAsync(int nopProductId);

    Task InsertAsync(AkeneoManagedAsset asset);
    Task UpdateAsync(AkeneoManagedAsset asset);
    Task DeleteAsync(AkeneoManagedAsset asset);
}
