using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public interface IAkeneoProductSectionSynchronizer
{
    int Order { get; }

    Task PrepareAsync(
        AkeneoProductSyncContext context,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    Task SynchronizeAsync(
        AkeneoProductSyncContext context,
        CancellationToken cancellationToken = default);


}