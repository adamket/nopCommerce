using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public interface IAkeneoCatalogReconciliationService
{
    Task<int> ReconcileUnseenAsync(
        AkeneoProductBatchImportRequest request,
        AkeneoProductBatchImportResult result,
        CancellationToken cancellationToken = default);
}
