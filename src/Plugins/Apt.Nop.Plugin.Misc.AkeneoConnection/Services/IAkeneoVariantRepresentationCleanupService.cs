using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public interface IAkeneoVariantRepresentationCleanupService
{
    Task CleanupPreviousRepresentationAsync(
        AkeneoProductSyncState previousState,
        AkeneoProductImportResult desiredResult,
        CancellationToken cancellationToken = default);
}
