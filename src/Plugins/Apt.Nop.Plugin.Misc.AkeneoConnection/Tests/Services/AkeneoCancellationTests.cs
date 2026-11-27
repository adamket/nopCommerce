using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

[TestFixture]
public class AkeneoCancellationTests
{
    [Test]
    public async Task The_batch_returns_the_original_cancellation_instead_of_discarding_its_details()
    {
        var exception = new TaskCanceledException("HTTP request timed out", new TimeoutException("30-second request budget"));
        var fanOut = new Mock<IAkeneoProductModelDeltaFanOutService>();
        fanOut.Setup(service => service.ShouldRun(It.IsAny<AkeneoProductBatchImportRequest>())).Throws(exception);
        var logs = new Mock<IAkeneoSyncItemLogService>();
        var batch = new AkeneoProductBatchSyncService(null!, null!, null!, logs.Object, null!, null!, null!, null!, null!,
            null!, null!, null!, null!, null!, fanOut.Object, null!, null!, null!, null!);
        var result = await batch.SyncProductsAsync(new AkeneoProductBatchImportRequest { SyncRunRecordId = 41 });
        Assert.Multiple(() =>
        {
            Assert.That(result.Canceled, Is.True);
            Assert.That(result.CompletedAllPages, Is.False);
            Assert.That(result.CancellationException, Is.SameAs(exception));
            Assert.That(result.Messages.Single(), Does.StartWith("Run complete (canceled)."));
            Assert.That(result.Errors.Single(), Does.Contain("TaskCanceledException").And.Contain("HTTP request timed out"));
        });
    }

    [Test]
    public void Run_cancellation_is_distinguished_from_independent_operation_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var details = AkeneoCancellationDetails.Describe(new OperationCanceledException(cancellation.Token), cancellation.Token);
        Assert.That(details, Does.Contain("run cancellation token was signaled").And.Not.Contain("HTTP timeout"));
    }
}
