using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public interface IAkeneoProductImportExecutionService
{
    Task<AkeneoProductImportExecutionResult> ImportProductsByProfileAsync(
        int profileId,
        SyncType syncType,
        CancellationToken cancellationToken = default);

    Task<AkeneoProductImportExecutionResult> ImportProductsAsync(
        AkeneoProductBatchImportRequest request,
        SyncType syncType,
        int? profileId = null,
        CancellationToken cancellationToken = default);
}