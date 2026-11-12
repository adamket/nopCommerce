using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public interface IAkeneoManagedAssetService
{
    /// <summary>Product-level rows only; combination rows are left out.</summary>
    Task<IList<AkeneoManagedAsset>> GetByProductAndMappingAsync(
        int nopProductId,
        string assetMappingKey);

    /// <summary>A product-level row only; combination rows are left out.</summary>
    Task<AkeneoManagedAsset> GetByIdentityAsync(
        int nopProductId,
        string assetMappingKey,
        string sourceIdentityHash);

    /// <summary>Product-level rows only; combination rows are left out.</summary>
    Task<IList<AkeneoManagedAsset>> GetByProductAsync(int nopProductId);

    /// <summary>Every row on the product, combination rows included.</summary>
    Task<IList<AkeneoManagedAsset>> GetAllByProductAsync(int nopProductId);

    /// <summary>The rows of one attribute combination.</summary>
    Task<IList<AkeneoManagedAsset>> GetByCombinationAsync(int combinationId);

    /// <summary>Every row, product-level or combination, that uses the picture.</summary>
    Task<IList<AkeneoManagedAsset>> GetByPictureAsync(int nopPictureId);

    Task InsertAsync(AkeneoManagedAsset asset);
    Task UpdateAsync(AkeneoManagedAsset asset);
    Task DeleteAsync(AkeneoManagedAsset asset);
}
