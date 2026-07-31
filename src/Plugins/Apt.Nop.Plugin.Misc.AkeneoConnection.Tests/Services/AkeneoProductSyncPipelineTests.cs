using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.TestData;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;

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
        var pipeline = new AkeneoProductSyncPipeline(new[] { first.Object, second.Object });

        await pipeline.PrepareAsync(Context());

        Assert.That(calls, Is.EqualTo(new[] { 100, 200 }));
    }

    [Test]
    public async Task SynchronizeAsync_executes_synchronizers_in_order()
    {
        var calls = new List<int>();
        var first = Synchronizer(order: 300, synchronize: () => calls.Add(300));
        var second = Synchronizer(order: 100, synchronize: () => calls.Add(100));
        var pipeline = new AkeneoProductSyncPipeline(new[] { first.Object, second.Object });

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
        var pipeline = new AkeneoProductSyncPipeline(new[] { first.Object, second.Object });

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
        var pipeline = new AkeneoProductSyncPipeline(new[] { first.Object, second.Object });

        await pipeline.PrepareAsync(context);

        second.Verify(
            synchronizer => synchronizer.PrepareAsync(
                It.IsAny<AkeneoProductSyncContext>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public void SynchronizeAsync_honors_cancellation_before_invocation()
    {
        var synchronizer = Synchronizer(order: 100);
        var pipeline = new AkeneoProductSyncPipeline(new[] { synchronizer.Object });
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
