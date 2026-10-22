using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.TestData;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core.Domain.Catalog;
using Nop.Core.Events;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

[TestFixture]
public class AkeneoProductSyncPipelineTests
{
    [Test]
    public async Task PrepareAsync_executes_synchronizers_in_order()
    {
        var calls = new List<int>();
        var first = Synchronizer(order: 200, prepare: () => calls.Add(200));
        var second = Synchronizer(order: 100, prepare: () => calls.Add(100));
        var pipeline = new AkeneoProductSyncPipeline(new[] { first.Object, second.Object }, Mock.Of<IEventPublisher>());

        await pipeline.PrepareAsync(Context());

        Assert.That(calls, Is.EqualTo(new[] { 100, 200 }));
    }

    [Test]
    public async Task SynchronizeAsync_executes_synchronizers_in_order()
    {
        var calls = new List<int>();
        var first = Synchronizer(order: 300, synchronize: () => calls.Add(300));
        var second = Synchronizer(order: 100, synchronize: () => calls.Add(100));
        var pipeline = new AkeneoProductSyncPipeline(new[] { first.Object, second.Object }, Mock.Of<IEventPublisher>());

        await pipeline.SynchronizeAsync(Context());

        Assert.That(calls, Is.EqualTo(new[] { 100, 300 }));
    }

    [Test]
    public async Task SynchronizeAsync_stops_after_error_is_added()
    {
        var context = Context();
        var first = Synchronizer(
            order: 100,
            synchronize: () => context.Result.AddError("failed"));
        var second = Synchronizer(order: 200);
        var pipeline = new AkeneoProductSyncPipeline(new[] { first.Object, second.Object }, Mock.Of<IEventPublisher>());

        await pipeline.SynchronizeAsync(context);

        second.Verify(
            synchronizer => synchronizer.SynchronizeAsync(
                It.IsAny<AkeneoProductSyncContext>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task PrepareAsync_stops_when_context_requests_stop()
    {
        var context = Context();
        var first = Synchronizer(
            order: 100,
            prepare: () => context.StopProcessing = true);
        var second = Synchronizer(order: 200);
        var pipeline = new AkeneoProductSyncPipeline(new[] { first.Object, second.Object }, Mock.Of<IEventPublisher>());

        await pipeline.PrepareAsync(context);

        second.Verify(
            synchronizer => synchronizer.PrepareAsync(
                It.IsAny<AkeneoProductSyncContext>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task SynchronizeAsync_publishes_one_product_update_event_after_all_sections()
    {
        var context = Context();
        context.Product = new Product { Id = 10 };
        var events = new Mock<IEventPublisher>();
        var eventsSeenBySecondSection = -1;
        var first = Synchronizer(
            order: 100,
            synchronize: () => context.ProductUpdateEventPending = true);
        var second = Synchronizer(
            order: 200,
            synchronize: () =>
            {
                eventsSeenBySecondSection = events.Invocations.Count;
                context.ProductUpdateEventPending = true;
            });
        var pipeline = new AkeneoProductSyncPipeline(new[] { first.Object, second.Object }, events.Object);

        await pipeline.SynchronizeAsync(context);

        Assert.Multiple(() =>
        {
            Assert.That(eventsSeenBySecondSection, Is.Zero, "No event is published between sections.");
            events.Verify(
                publisher => publisher.PublishAsync(
                    It.Is<EntityUpdatedEvent<Product>>(e => e.Entity == context.Product)),
                Times.Once);
            Assert.That(context.ProductUpdateEventPending, Is.False);
        });
    }

    [Test]
    public void SynchronizeAsync_publishes_pending_update_when_a_later_section_fails()
    {
        var context = Context();
        context.Product = new Product { Id = 10 };
        var events = new Mock<IEventPublisher>();
        var first = Synchronizer(
            order: 100,
            synchronize: () => context.ProductUpdateEventPending = true);
        var second = Synchronizer(
            order: 200,
            synchronize: () => throw new InvalidOperationException("boom"));
        var pipeline = new AkeneoProductSyncPipeline(new[] { first.Object, second.Object }, events.Object);

        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await pipeline.SynchronizeAsync(context));

        events.Verify(
            publisher => publisher.PublishAsync(It.IsAny<EntityUpdatedEvent<Product>>()),
            Times.Once);
    }

    [Test]
    public async Task SynchronizeAsync_publishes_nothing_when_no_section_saved_the_product()
    {
        var context = Context();
        context.Product = new Product { Id = 10 };
        var events = new Mock<IEventPublisher>(MockBehavior.Strict);
        var pipeline = new AkeneoProductSyncPipeline(
            new[] { Synchronizer(order: 100).Object },
            events.Object);

        await pipeline.SynchronizeAsync(context);

        events.VerifyNoOtherCalls();
    }

    [Test]
    public void SynchronizeAsync_honors_cancellation_before_invocation()
    {
        var synchronizer = Synchronizer(order: 100);
        var pipeline = new AkeneoProductSyncPipeline(new[] { synchronizer.Object }, Mock.Of<IEventPublisher>());
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(
            async () => await pipeline.SynchronizeAsync(Context(), source.Token));
    }

    private static Mock<IAkeneoProductSectionSynchronizer> Synchronizer(
        int order,
        Action? prepare = null,
        Action? synchronize = null)
    {
        var mock = new Mock<IAkeneoProductSectionSynchronizer>(MockBehavior.Strict);
        mock.SetupGet(item => item.Order).Returns(order);
        mock.Setup(item => item.PrepareAsync(
                It.IsAny<AkeneoProductSyncContext>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => prepare?.Invoke())
            .Returns(Task.CompletedTask);
        mock.Setup(item => item.SynchronizeAsync(
                It.IsAny<AkeneoProductSyncContext>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => synchronize?.Invoke())
            .Returns(Task.CompletedTask);
        return mock;
    }

    private static AkeneoProductSyncContext Context() => new()
    {
        Source = AkeneoTestData.Product(),
        Request = new AkeneoProductImportRequest(),
        Result = new AkeneoProductImportResult()
    };
}
