using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public interface IAkeneoProductSyncPipeline
{
    Task SynchronizeAsync(
        AkeneoProductSyncContext context,
        CancellationToken cancellationToken = default);

    public Task PrepareAsync(
        AkeneoProductSyncContext context,
        CancellationToken cancellationToken = default);
}