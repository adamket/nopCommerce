using Microsoft.AspNetCore.Mvc;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Controllers;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Nop.Core;
using Nop.Core.Domain.Customers;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

[TestFixture]
public class AkeneoStopCurrentSyncTests
{
    [Test]
    public async Task A_stale_request_cannot_stop_the_new_holder()
    {
        var leases = new Mock<IAkeneoSyncLeaseService>();
        leases.Setup(s => s.GetLeaseAsync(AkeneoConnectionConstants.CatalogWriterLockKey))
            .ReturnsAsync(new AkeneoSyncLease { Id = 8, SyncRunRecordId = 42 });
        var logs = new Mock<IAkeneoSyncItemLogService>(MockBehavior.Strict);
        var controller = new AkeneoSyncProfileController(null!, null!, null!, null!, null!, null!,
            leases.Object, null!, null!, logs.Object, null!);

        Assert.That(await controller.StopCurrentSync(41), Is.TypeOf<ConflictObjectResult>());
        logs.VerifyNoOtherCalls();
        leases.Verify(s => s.ReleaseAsync(It.IsAny<AkeneoSyncLease>()), Times.Never);
    }

    [Test]
    public async Task Repeated_stop_records_the_administrator_once_and_leaves_the_lease_with_the_worker()
    {
        const int runId = 41;
        var leases = new Mock<IAkeneoSyncLeaseService>();
        leases.Setup(s => s.GetLeaseAsync(AkeneoConnectionConstants.CatalogWriterLockKey))
            .ReturnsAsync(new AkeneoSyncLease { Id = 8, SyncRunRecordId = runId });
        var runs = new Mock<IAkeneoSyncRunRecordService>();
        runs.Setup(s => s.GetAkeneoSyncRunRecordByIdAsync(runId))
            .ReturnsAsync(new AkeneoSyncRunRecord { Id = runId, SyncStatusId = (int)SyncStatus.Started });
        var work = new Mock<IWorkContext>();
        work.Setup(s => s.GetCurrentCustomerAsync()).ReturnsAsync(new Customer { Id = 7 });
        var logs = new Mock<IAkeneoSyncItemLogService>();
        AkeneoSyncItemLog? saved = null;
        logs.Setup(s => s.GetStopRequestAsync(runId)).ReturnsAsync(() => saved?.Message);
        logs.Setup(s => s.InsertAkeneoSyncItemLogAsync(It.IsAny<AkeneoSyncItemLog>()))
            .Callback<AkeneoSyncItemLog>(entry => saved = entry).Returns(Task.CompletedTask);
        var controller = new AkeneoSyncProfileController(null!, null!, null!, runs.Object, null!, null!,
            leases.Object, null!, null!, logs.Object, work.Object);

        Assert.That(await controller.StopCurrentSync(runId), Is.TypeOf<JsonResult>());
        Assert.That(await controller.StopCurrentSync(runId), Is.TypeOf<JsonResult>());
        Assert.That(saved!.SyncRunRecordId, Is.EqualTo(runId));
        Assert.That(saved.ActionTypeId, Is.EqualTo((int)SyncItemActionType.StopRequested));
        Assert.That(saved.Message, Does.Contain("customer ID 7"));
        logs.Verify(s => s.InsertAkeneoSyncItemLogAsync(It.IsAny<AkeneoSyncItemLog>()), Times.Once);
        leases.Verify(s => s.ReleaseAsync(It.IsAny<AkeneoSyncLease>()), Times.Never);
        leases.Verify(s => s.ForceReleaseAsync(It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Never);
    }
}
