using System.Transactions;
using Nop.Core.Domain.Catalog;
using Nop.Core.Events;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public interface IAkeneoDeferredProductEvents
{
    /// <summary>Queues one product updated event for after the current unit.</summary>
    void Defer(Product product);

    /// <summary>Publishes the queued events, each product once.</summary>
    Task PublishAsync();

    /// <summary>Drops the queued events, for a unit that rolled back and so updated nothing.</summary>
    void Discard();
}

/// <summary>
/// The sync writes a product inside its unit's transaction. Published there, the
/// product updated event clears caches before the change is saved; on a database
/// with read-committed snapshot a page read in between caches the old product
/// again for the default cache time, and handlers that clear other instances run
/// too early as well. The batch sync publishes the queued events once the unit has
/// saved, and drops them when it rolled back. Only products a section actually
/// changed are queued, so skipped and unchanged products never clear caches.
/// </summary>
public class AkeneoDeferredProductEvents(IEventPublisher eventPublisher) : IAkeneoDeferredProductEvents
{
    private readonly Dictionary<int, Product> _pending = new();

    public void Defer(Product product)
    {
        if (product is { Id: > 0 })
            _pending[product.Id] = product;
    }

    public async Task PublishAsync()
    {
        if (_pending.Count == 0)
            return;

        var products = _pending.Values.ToList();
        _pending.Clear();

        // Outside any transaction, so handlers read and clear what is actually saved.
        using var noTransaction = new TransactionScope(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled);
        foreach (var product in products)
            await eventPublisher.EntityUpdatedAsync(product);
    }

    public void Discard() => _pending.Clear();
}
