using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
public interface IAkeneoProductImportService
{
    Task<AkeneoProductImportResult> ImportProductByUuidAsync(
        AkeneoProductImportRequest request,
        CancellationToken cancellationToken = default);
}