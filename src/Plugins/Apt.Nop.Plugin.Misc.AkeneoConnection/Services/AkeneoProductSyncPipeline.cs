using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public class AkeneoProductSyncPipeline(
    IEnumerable<IAkeneoProductSectionSynchronizer> synchronizers)
    : IAkeneoProductSyncPipeline
{
    private readonly IReadOnlyList<IAkeneoProductSectionSynchronizer>
        _synchronizers = synchronizers
            .OrderBy(synchronizer => synchronizer.Order)
            .ToList();

    public async Task SynchronizeAsync(
        AkeneoProductSyncContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var synchronizer in _synchronizers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (context.StopProcessing ||
                context.Result.Errors.Any())
            {
                return;
            }

            await synchronizer.SynchronizeAsync(
                context,
                cancellationToken);
        }
    }

    public async Task PrepareAsync(
        AkeneoProductSyncContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var synchronizer in _synchronizers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (context.StopProcessing || context.Result.Errors.Any())
                return;

            await synchronizer.PrepareAsync(context, cancellationToken);
        }
    }
}