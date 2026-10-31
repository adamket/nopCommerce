using System.Transactions;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core.Domain.Catalog;
using Nop.Core.Events;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

/// <summary>
/// The product updated event a sync unit raises is published once the unit's
/// transaction has finished, so cache clears run after the change is saved.
/// </summary>
[TestFixture]
public class AkeneoDeferredProductEventsTests
{
    private sealed class SavesProductSilently : IAkeneoProductSectionSynchronizer
    {
        public int Order => 100;

        public Task SynchronizeAsync(AkeneoProductSyncContext context, CancellationToken cancellationToken = default)
        {
            context.ProductUpdateEventPending = true;
            return Task.CompletedTask;
        }
    }

    private static AkeneoProductSyncContext Context(Product product) => new()
    {
        Product = product,
        Request = new AkeneoProductImportRequest(),
        Result = new AkeneoProductImportResult()
    };

    private static int Published(Mock<IEventPublisher> publisher, int productId) =>
        publisher.Invocations.Count(call =>
            call.Arguments.FirstOrDefault() is EntityUpdatedEvent<Product> updated && updated.Entity.Id == productId);

    [Test]
    public async Task Queued_events_are_published_once_per_product()
    {
        var publisher = new Mock<IEventPublisher>();
        var queue = new AkeneoDeferredProductEvents(publisher.Object);
        var product = new Product { Id = 2127 };

        queue.Defer(product);
        queue.Defer(product);
        queue.Defer(new Product());
        await queue.PublishAsync();
        await queue.PublishAsync();

        Assert.Multiple(() =>
        {
            Assert.That(Published(publisher, 2127), Is.EqualTo(1));
            Assert.That(publisher.Invocations, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task A_rolled_back_unit_s_events_are_dropped()
    {
        var publisher = new Mock<IEventPublisher>();
        var queue = new AkeneoDeferredProductEvents(publisher.Object);

        queue.Defer(new Product { Id = 2131 });
        queue.Discard();
        await queue.PublishAsync();

        Assert.That(publisher.Invocations, Is.Empty);
    }

    [Test]
    public async Task Inside_a_transaction_the_pipeline_queues_the_event_until_it_has_finished()
    {
        var publisher = new Mock<IEventPublisher>();
        var queue = new AkeneoDeferredProductEvents(publisher.Object);
        var pipeline = new AkeneoProductSyncPipeline([new SavesProductSilently()], publisher.Object, queue);

        using (new TransactionScope(TransactionScopeOption.Required, TransactionScopeAsyncFlowOption.Enabled))
        {
            await pipeline.SynchronizeAsync(Context(new Product { Id = 2127 }));
            Assert.That(Published(publisher, 2127), Is.Zero);
        }

        await queue.PublishAsync();
        Assert.That(Published(publisher, 2127), Is.EqualTo(1));
    }

    [Test]
    public async Task Outside_a_transaction_the_pipeline_publishes_at_once()
    {
        var publisher = new Mock<IEventPublisher>();
        var pipeline = new AkeneoProductSyncPipeline(
            [new SavesProductSilently()], publisher.Object, new AkeneoDeferredProductEvents(publisher.Object));

        await pipeline.SynchronizeAsync(Context(new Product { Id = 2127 }));

        Assert.That(Published(publisher, 2127), Is.EqualTo(1));
    }
}
