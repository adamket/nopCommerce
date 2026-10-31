using System.Transactions;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core.Events;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public class AkeneoProductSyncPipeline(
    IEnumerable<IAkeneoProductSectionSynchronizer> synchronizers,
    IEventPublisher eventPublisher,
    IAkeneoDeferredProductEvents deferredProductEvents = null)
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
            // Sections save the product row without an event; raise one here, even
            // when a later section failed, so the product's caches are cleared once
            // rather than once per section. Inside a sync unit's transaction it is
            // queued and published once the transaction has finished.
            if (context.ProductUpdateEventPending && context.Product != null)
            {
                context.ProductUpdateEventPending = false;
                if (Transaction.Current != null && deferredProductEvents != null)
                    deferredProductEvents.Defer(context.Product);
                else
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
