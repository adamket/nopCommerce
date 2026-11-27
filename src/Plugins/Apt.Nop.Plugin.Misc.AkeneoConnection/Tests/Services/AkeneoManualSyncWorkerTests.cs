using Apt.Nop.Plugin.Misc.AkeneoConnection.Controllers;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Nop.Services.ScheduleTasks;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

[TestFixture]
public class AkeneoManualSyncWorkerTests
{
    private static TaskCompletionSource<bool> Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static AkeneoProductSyncExecutionResult Completed() => new() { Success = true, SyncStatus = SyncStatus.Completed };

    private sealed class ScopeProbe : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    private static async Task<AkeneoManualSyncJob> Finished(AkeneoManualSyncWorker worker, Guid id)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (worker.GetJob(id)?.Result == null)
            await Task.Delay(10, timeout.Token);
        return worker.GetJob(id)!;
    }

    [Test]
    public async Task Accepted_manual_run_survives_request_cancellation_and_uses_a_disposed_separate_scope()
    {
        var started = Signal();
        var finish = Signal();
        var execution = new Mock<IAkeneoProductSyncExecutionService>();
        var accessor = new HttpContextAccessor();
        ScopeProbe? jobProbe = null;
        CancellationToken jobToken = default;
        execution.Setup(service => service.ImportProductsByProfileAsync(7, SyncType.ManualProfileSync, It.IsAny<CancellationToken>()))
            .Returns(async (int _, SyncType _, CancellationToken token) =>
            {
                jobToken = token;
                jobProbe = accessor.HttpContext!.RequestServices.GetRequiredService<ScopeProbe>();
                Assert.That(accessor.HttpContext.Request.Path.Value, Is.EqualTo("/" + NopTaskDefaults.ScheduleTaskPath));
                started.SetResult(true);
                await finish.Task;
                token.ThrowIfCancellationRequested();
                return Completed();
            });
        var services = new ServiceCollection();
        services.AddScoped<ScopeProbe>();
        services.AddScoped(_ => execution.Object);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var worker = new AkeneoManualSyncWorker(provider.GetRequiredService<IServiceScopeFactory>(), accessor,
            NullLogger<AkeneoManualSyncWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            using var requestScope = provider.CreateScope();
            using var requestCancellation = new CancellationTokenSource();
            accessor.HttpContext = new DefaultHttpContext { RequestServices = requestScope.ServiceProvider, RequestAborted = requestCancellation.Token };
            var requestProbe = requestScope.ServiceProvider.GetRequiredService<ScopeProbe>();
            var url = new Mock<IUrlHelper>();
            url.Setup(helper => helper.Action(It.IsAny<UrlActionContext>())).Returns("/admin/AkeneoSync/ManualSyncStatus/test");
            var controller = new AkeneoSyncController(new AkeneoConnectionSettings(), worker, null!, null!, null!, null!, null!) { Url = url.Object };
            var accepted = (ObjectResult)controller.ImportProductsByProfile(7, false);
            Assert.That(accepted.StatusCode, Is.EqualTo(202));
            var id = (Guid)accepted.Value!.GetType().GetProperty("jobId")!.GetValue(accepted.Value)!;
            requestCancellation.Cancel();
            accessor.HttpContext = null;
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Multiple(() =>
            {
                Assert.That(jobToken.IsCancellationRequested, Is.False);
                Assert.That(jobProbe, Is.Not.SameAs(requestProbe));
                Assert.That(jobProbe!.Disposed, Is.False);
                Assert.That(worker.TryEnqueue(new(7, AkeneoManualSyncKind.Profile)), Is.Null, "duplicate clicks cannot queue another run");
            });
            finish.SetResult(true);
            var completed = await Finished(worker, id);
            Assert.That(completed.Result!.Success, Is.True);
            Assert.That(jobProbe!.Disposed, Is.True);
            Assert.That(accessor.HttpContext, Is.Null);
        }
        finally
        {
            finish.TrySetResult(true);
            accessor.HttpContext = null;
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task Host_shutdown_cancels_the_job_and_refuses_new_jobs()
    {
        var started = Signal();
        var execution = new Mock<IAkeneoProductSyncExecutionService>();
        execution.Setup(service => service.SyncProductByUuidAsync(7, "uuid", It.IsAny<CancellationToken>()))
            .Returns(async (int _, string _, CancellationToken token) =>
            {
                started.SetResult(true);
                await Task.Delay(Timeout.Infinite, token);
                return Completed();
            });
        var services = new ServiceCollection().AddScoped(_ => execution.Object);
        await using var provider = services.BuildServiceProvider();
        using var worker = new AkeneoManualSyncWorker(provider.GetRequiredService<IServiceScopeFactory>(), new HttpContextAccessor(), NullLogger<AkeneoManualSyncWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);
        var job = worker.TryEnqueue(new(7, AkeneoManualSyncKind.Product, SourceKey: "uuid"))!;
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        var result = worker.GetJob(job.Id)!.Result!;
        Assert.Multiple(() =>
        {
            Assert.That(result.Canceled, Is.True);
            Assert.That(result.Message, Does.Contain("application is shutting down"));
            Assert.That(worker.TryEnqueue(new(7, AkeneoManualSyncKind.Profile)), Is.Null);
        });
    }

    [Test]
    public async Task A_failed_job_does_not_stop_the_worker_and_resync_options_are_snapshotted()
    {
        var execution = new Mock<IAkeneoProductSyncExecutionService>();
        execution.Setup(service => service.SyncProductModelByCodeAsync(7, "parent", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("test failure"));
        execution.Setup(service => service.ResyncProfileAsync(7, It.IsAny<AkeneoResyncOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Completed());
        var services = new ServiceCollection().AddScoped(_ => execution.Object);
        await using var provider = services.BuildServiceProvider();
        using var worker = new AkeneoManualSyncWorker(provider.GetRequiredService<IServiceScopeFactory>(), new HttpContextAccessor(), NullLogger<AkeneoManualSyncWorker>.Instance);
        var failed = worker.TryEnqueue(new(7, AkeneoManualSyncKind.ProductModel, SourceKey: "parent"))!;
        Assert.That(worker.TryEnqueue(new(7, AkeneoManualSyncKind.Profile)), Is.Null, "queued jobs also block duplicate clicks");
        await worker.StartAsync(CancellationToken.None);
        try
        {
            Assert.That((await Finished(worker, failed.Id)).Result!.Message, Does.Contain("test failure"));
            var families = new[] { "trees" };
            var resync = worker.TryEnqueue(new(7, AkeneoManualSyncKind.Resync, RepairDestination: true, UpdateExistingOnly: true, FamilyCodes: families))!;
            families[0] = "changed after enqueue";
            Assert.That((await Finished(worker, resync.Id)).Result!.Success, Is.True);
            execution.Verify(service => service.ResyncProfileAsync(7,
                It.Is<AkeneoResyncOptions>(options => options.RepairDestination && options.UpdateExistingOnly && options.FamilyCodes.Single() == "trees"),
                It.IsAny<CancellationToken>()), Times.Once);
        }
        finally { await worker.StopAsync(CancellationToken.None); }
    }
    private static AkeneoSyncController Controller(AkeneoManualSyncWorker worker)
    {
        var url = new Mock<IUrlHelper>();
        url.Setup(helper => helper.Action(It.IsAny<UrlActionContext>()))
            .Returns((UrlActionContext action) => "/status/" + new Microsoft.AspNetCore.Routing.RouteValueDictionary(action.Values)["id"]);
        return new AkeneoSyncController(new AkeneoConnectionSettings(), worker, null!, null!, null!, null!, null!) { Url = url.Object };
    }

    private static System.Text.Json.JsonElement Payload(IActionResult response) =>
        System.Text.Json.JsonSerializer.SerializeToElement(response switch
        {
            JsonResult json => json.Value,
            ObjectResult result => result.Value,
            _ => throw new InvalidOperationException("Unexpected response type")
        });

    [TestCase(AkeneoManualSyncKind.Profile)]
    [TestCase(AkeneoManualSyncKind.Resync)]
    public async Task A_refreshed_profile_finds_the_queued_and_running_job_and_its_finished_result(AkeneoManualSyncKind kind)
    {
        var started = Signal();
        var finish = new TaskCompletionSource<AkeneoProductSyncExecutionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var execution = new Mock<IAkeneoProductSyncExecutionService>();
        execution.Setup(service => service.ImportProductsByProfileAsync(7, SyncType.ManualProfileSync, It.IsAny<CancellationToken>()))
            .Returns(() => { started.TrySetResult(true); return finish.Task; });
        execution.Setup(service => service.ResyncProfileAsync(7, It.IsAny<AkeneoResyncOptions>(), It.IsAny<CancellationToken>()))
            .Returns(() => { started.TrySetResult(true); return finish.Task; });
        var services = new ServiceCollection().AddScoped(_ => execution.Object);
        await using var provider = services.BuildServiceProvider();
        using var worker = new AkeneoManualSyncWorker(provider.GetRequiredService<IServiceScopeFactory>(), new HttpContextAccessor(), NullLogger<AkeneoManualSyncWorker>.Instance);
        var job = worker.TryEnqueue(new(7, kind))!;
        var queued = Payload(Controller(worker).LatestProfileSync(7));
        Assert.That(queued.GetProperty("pending").GetBoolean(), Is.True);
        Assert.That(queued.GetProperty("statusUrl").GetString(), Is.EqualTo("/status/" + job.Id));
        Assert.That(Payload(Controller(worker).LatestProfileSync(8)).GetProperty("idle").GetBoolean(), Is.True);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // A new controller models a fresh page request with no knowledge of the job ID.
            Assert.That(Payload(Controller(worker).LatestProfileSync(7)).GetProperty("pending").GetBoolean(), Is.True);
            finish.SetResult(new AkeneoProductSyncExecutionResult
            {
                Success = true, SyncStatus = SyncStatus.CompletedWithWarnings, SyncRunRecordId = 41,
                UpdatedCount = 17, Message = "Run finished", Warnings = new List<string> { "Review one product" }
            });
            await Finished(worker, job.Id);
            var result = Payload(Controller(worker).LatestProfileSync(7));
            Assert.Multiple(() =>
            {
                Assert.That(result.GetProperty("success").GetBoolean(), Is.True);
                Assert.That(result.GetProperty("counts").GetProperty("updated").GetInt32(), Is.EqualTo(17));
                Assert.That(result.GetProperty("syncRunRecordId").GetInt32(), Is.EqualTo(41));
                Assert.That(result.GetProperty("warnings")[0].GetString(), Is.EqualTo("Review one product"));
                Assert.That(execution.Invocations, Has.Count.EqualTo(1), "reconnecting must only read status");
            });
        }
        finally
        {
            finish.TrySetResult(Completed());
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task Latest_profile_job_excludes_other_profiles_and_preview_imports_and_prefers_a_new_job()
    {
        var execution = new Mock<IAkeneoProductSyncExecutionService>();
        execution.Setup(service => service.ImportProductsByProfileAsync(It.IsAny<int>(), It.IsAny<SyncType>(), It.IsAny<CancellationToken>())).ReturnsAsync(Completed());
        execution.Setup(service => service.SyncProductByUuidAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(Completed());
        var services = new ServiceCollection().AddScoped(_ => execution.Object);
        await using var provider = services.BuildServiceProvider();
        using var worker = new AkeneoManualSyncWorker(provider.GetRequiredService<IServiceScopeFactory>(), new HttpContextAccessor(), NullLogger<AkeneoManualSyncWorker>.Instance);
        Assert.That(worker.GetLatestProfileJob(7), Is.Null);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            var profile = worker.TryEnqueue(new(7, AkeneoManualSyncKind.Profile))!;
            await Finished(worker, profile.Id);
            var preview = worker.TryEnqueue(new(7, AkeneoManualSyncKind.Product, SourceKey: "uuid"))!;
            await Finished(worker, preview.Id);
            var otherProfile = worker.TryEnqueue(new(8, AkeneoManualSyncKind.Profile))!;
            await Finished(worker, otherProfile.Id);
            Assert.That(worker.GetLatestProfileJob(7)!.Id, Is.EqualTo(profile.Id));
            Assert.That(worker.GetLatestProfileJob(8)!.Id, Is.EqualTo(otherProfile.Id));
            var newer = worker.TryEnqueue(new(7, AkeneoManualSyncKind.Profile))!;
            await Finished(worker, newer.Id);
            Assert.That(worker.GetLatestProfileJob(7)!.Id, Is.EqualTo(newer.Id));
        }
        finally { await worker.StopAsync(CancellationToken.None); }
    }

    [TestCase(SyncStatus.Cancelled)]
    [TestCase(SyncStatus.Failed)]
    public async Task A_refresh_after_completion_returns_cancellation_or_failure_details(SyncStatus status)
    {
        var execution = new Mock<IAkeneoProductSyncExecutionService>();
        execution.Setup(service => service.ImportProductsByProfileAsync(7, SyncType.ManualProfileSync, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AkeneoProductSyncExecutionResult
            {
                Canceled = status == SyncStatus.Cancelled, SyncStatus = status, Message = "Original diagnostic message",
                Errors = new List<string> { "Original exception details" }
            });
        var services = new ServiceCollection().AddScoped(_ => execution.Object);
        await using var provider = services.BuildServiceProvider();
        using var worker = new AkeneoManualSyncWorker(provider.GetRequiredService<IServiceScopeFactory>(), new HttpContextAccessor(), NullLogger<AkeneoManualSyncWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            var job = worker.TryEnqueue(new(7, AkeneoManualSyncKind.Profile))!;
            await Finished(worker, job.Id);
            var result = Payload(Controller(worker).LatestProfileSync(7));
            Assert.That(result.GetProperty("message").GetString(), Is.EqualTo("Original diagnostic message"));
            Assert.That(result.GetProperty("errors")[0].GetString(), Is.EqualTo("Original exception details"));
        }
        finally { await worker.StopAsync(CancellationToken.None); }
    }

}
