using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core.Domain.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

/// <summary>
/// Gives a variant represented as an attribute combination the pictures of its
/// "Variant products" asset mappings. Each image is added to the parent
/// product's gallery and linked to the combination
/// (<see cref="ProductAttributeCombinationPicture"/>), in display order.
/// </summary>
public interface IAkeneoCombinationPictureSynchronizer
{
    /// <summary>
    /// Applies the leaf's images to the combination; returns true when
    /// anything in nopCommerce changed.
    /// </summary>
    Task<bool> SynchronizeCombinationAsync(
        AkeneoProductSyncContext leafContext,
        Product parentProduct,
        int combinationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops the plugin's picture rows of a combination that is being deleted,
    /// deleting each picture nothing else still uses.
    /// </summary>
    Task ReleaseCombinationAsync(
        int combinationId,
        CancellationToken cancellationToken = default);
}
