using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public interface IAkeneoProductSyncExecutionService
{
    Task<AkeneoProductSyncExecutionResult> ImportProductsByProfileAsync(
        int profileId,
        SyncType syncType,
        CancellationToken cancellationToken = default);

    Task<AkeneoProductSyncExecutionResult> ImportProductsAsync(
        AkeneoProductBatchImportRequest request,
        SyncType syncType,
        int? profileId = null,
        CancellationToken cancellationToken = default);
}