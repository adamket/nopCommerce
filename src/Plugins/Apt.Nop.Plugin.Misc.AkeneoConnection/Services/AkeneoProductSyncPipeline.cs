using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core.Events;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public class AkeneoProductSyncPipeline(
    IEnumerable<IAkeneoProductSectionSynchronizer> synchronizers,
    IEventPublisher eventPublisher)
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

        try
        {
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
        finally
        {
            // Sections save the product row without an event; publish one here,
            // even when a later section failed, so the product's caches are
            // cleared once rather than once per section.
            if (context.ProductUpdateEventPending && context.Product != null)
            {
                context.ProductUpdateEventPending = false;
                await eventPublisher.EntityUpdatedAsync(context.Product);
            }
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
