using Nop.Core.Domain.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

/// <summary>Version-specific attribute value picture storage and reference protection.</summary>
public interface IAkeneoAttributeValuePictureStore
{
    Task<IList<int>> GetPictureIdsAsync(ProductAttributeValue value);
    Task SetPictureIdsAsync(ProductAttributeValue value, IReadOnlyList<int> pictureIds);
    Task<bool> IsReferencedAsync(int pictureId, int? excludingProductId = null);
    Task<HashSet<int>> GetProductPictureIdsAsync(int productId);
}
