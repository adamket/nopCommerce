using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Nop.Data;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

/// <summary>
/// Persists plugin-owned media relationships. Ownership is product/mapping
/// based rather than profile based so running the same family through another
/// sync profile does not create duplicate pictures or videos.
/// </summary>
public sealed class AkeneoManagedAssetService(
    IRepository<AkeneoManagedAsset> repository)
    : IAkeneoManagedAssetService
{
    public async Task<IList<AkeneoManagedAsset>> GetByProductAndMappingAsync(
        int nopProductId,
        string assetMappingKey)
    {
        return await repository.Table
            .Where(item =>
                item.NopProductId == nopProductId &&
                item.AssetMappingKey == assetMappingKey)
            .ToListAsync();
    }

    public async Task<AkeneoManagedAsset> GetByIdentityAsync(
        int nopProductId,
        string assetMappingKey,
        string sourceIdentityHash)
    {
        return await repository.Table.FirstOrDefaultAsync(item =>
            item.NopProductId == nopProductId &&
            item.AssetMappingKey == assetMappingKey &&
            item.SourceIdentityHash == sourceIdentityHash);
    }

    public async Task<IList<AkeneoManagedAsset>> GetByProductAsync(
        int nopProductId)
    {
        return await repository.Table
            .Where(item => item.NopProductId == nopProductId)
            .ToListAsync();
    }

    public Task InsertAsync(AkeneoManagedAsset asset) =>
        repository.InsertAsync(asset);

    public Task UpdateAsync(AkeneoManagedAsset asset) =>
        repository.UpdateAsync(asset);

    public Task DeleteAsync(AkeneoManagedAsset asset) =>
        repository.DeleteAsync(asset);
}
