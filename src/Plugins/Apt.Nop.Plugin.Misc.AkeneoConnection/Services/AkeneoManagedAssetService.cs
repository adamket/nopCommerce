using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Nop.Data;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

/// <summary>
/// Persists plugin-owned media relationships. Ownership is product/mapping
/// based rather than profile based so running the same family through another
/// sync profile does not create duplicate pictures or videos.
/// Combination rows (<see cref="AkeneoManagedAsset.NopProductAttributeCombinationId"/>)
/// sit on the parent product but are only returned by the combination and
/// "all" queries, so product-level syncs never treat them as their own.
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
                item.NopProductAttributeCombinationId == null &&
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
            item.NopProductAttributeCombinationId == null &&
            item.AssetMappingKey == assetMappingKey &&
            item.SourceIdentityHash == sourceIdentityHash);
    }

    public async Task<IList<AkeneoManagedAsset>> GetByProductAsync(
        int nopProductId)
    {
        return await repository.Table
            .Where(item =>
                item.NopProductId == nopProductId &&
                item.NopProductAttributeCombinationId == null)
            .ToListAsync();
    }

    public async Task<IList<AkeneoManagedAsset>> GetAllByProductAsync(
        int nopProductId)
    {
        return await repository.Table
            .Where(item => item.NopProductId == nopProductId)
            .ToListAsync();
    }

    public async Task<IList<AkeneoManagedAsset>> GetByCombinationAsync(
        int combinationId)
    {
        return await repository.Table
            .Where(item => item.NopProductAttributeCombinationId == combinationId)
            .ToListAsync();
    }

    public async Task<IList<AkeneoManagedAsset>> GetByPictureAsync(
        int nopPictureId)
    {
        return await repository.Table
            .Where(item => item.NopPictureId == nopPictureId)
            .ToListAsync();
    }

    public Task InsertAsync(AkeneoManagedAsset asset) =>
        repository.InsertAsync(asset);

    public Task UpdateAsync(AkeneoManagedAsset asset) =>
        repository.UpdateAsync(asset);

    public Task DeleteAsync(AkeneoManagedAsset asset) =>
        repository.DeleteAsync(asset);
}
