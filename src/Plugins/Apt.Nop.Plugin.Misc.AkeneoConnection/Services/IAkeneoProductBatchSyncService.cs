using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
public interface IAkeneoProductBatchSyncService
{
    Task<AkeneoProductImportResult> SyncProductByUuidAsync(
        AkeneoProductImportRequest request,
        CancellationToken cancellationToken = default);

    Task<AkeneoProductBatchImportResult> SyncProductsAsync(
        AkeneoProductBatchImportRequest request,
        CancellationToken cancellationToken = default);
}