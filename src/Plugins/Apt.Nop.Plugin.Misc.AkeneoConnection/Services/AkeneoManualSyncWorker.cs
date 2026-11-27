using System.Threading.Channels;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nop.Services.ScheduleTasks;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public enum AkeneoManualSyncKind { Profile, Resync, Product, ProductModel }

// Only values cross the request boundary: never capture a controller, scoped service or request token.
public sealed record AkeneoManualSyncRequest(
    int ProfileId,
    AkeneoManualSyncKind Kind,
    bool FullSync = false,
    string SourceKey = null,
    bool RepairDestination = false,
    bool UpdateExistingOnly = false,
    string[] FamilyCodes = null);

public sealed record AkeneoManualSyncJob(
    Guid Id,
    AkeneoManualSyncRequest Request,
    DateTime QueuedOnUtc,
    bool Running = false,
    AkeneoProductSyncExecutionResult Result = null);

/// <summary>
/// One pending/running manual job per application instance. The execution service's
/// database lease also serializes writers against scheduled jobs and other instances.
/// Jobs and their recent response snapshots are in memory; permanent results live in sync history.
/// An application restart does not resume an interrupted job.
/// </summary>
public sealed class AkeneoManualSyncWorker(
    IServiceScopeFactory scopeFactory,
    IHttpContextAccessor httpContextAccessor,
    ILogger<AkeneoManualSyncWorker> logger) : BackgroundService
{
    private readonly object _gate = new();
    private readonly Channel<AkeneoManualSyncJob> _queue = Channel.CreateBounded<AkeneoManualSyncJob>(1);
    private readonly Dictionary<Guid, AkeneoManualSyncJob> _jobs = new();
    private Guid? _activeJobId;
    private bool _stopping;

    public AkeneoManualSyncJob TryEnqueue(AkeneoManualSyncRequest request)
    {
        lock (_gate)
        {
            if (_stopping || _activeJobId.HasValue)
                return null;

            // Bound memory while keeping finished responses available for polling.
            foreach (var expired in _jobs.Values.OrderByDescending(job => job.QueuedOnUtc).Skip(49).ToList())
                _jobs.Remove(expired.Id);

            var job = new AkeneoManualSyncJob(Guid.NewGuid(), request with
            {
                FamilyCodes = request.FamilyCodes?.ToArray()
            }, DateTime.UtcNow);
            _jobs.Add(job.Id, job);
            _activeJobId = job.Id;
            if (!_queue.Writer.TryWrite(job))
            {
                _jobs.Remove(job.Id);
                _activeJobId = null;
                return null;
            }
            return job;
        }
    }

    public AkeneoManualSyncJob GetJob(Guid id)
    {
        lock (_gate)
            return _jobs.GetValueOrDefault(id);
    }

    // A refresh can arrive after completion, so include the latest finished profile job too.
    // Individual preview imports have a different response shape and belong on the preview page.
    public AkeneoManualSyncJob GetLatestProfileJob(int profileId)
    {
        lock (_gate)
            return _jobs.Values
                .Where(job => job.Request.ProfileId == profileId &&
                    job.Request.Kind is AkeneoManualSyncKind.Profile or AkeneoManualSyncKind.Resync)
                .OrderByDescending(job => job.QueuedOnUtc)
                .FirstOrDefault();
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _stopping = true;
            _queue.Writer.TryComplete();
        }
        return base.StopAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var job in _queue.Reader.ReadAllAsync(stoppingToken))
                await RunJobAsync(job, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown; the active job records its cancellation before returning.
        }
    }

    private async Task RunJobAsync(AkeneoManualSyncJob job, CancellationToken stoppingToken)
    {
        lock (_gate)
            _jobs[job.Id] = job with { Running = true };

        AkeneoProductSyncExecutionResult result;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            // nopCommerce event consumers resolve services through EngineContext and HttpContext.
            // Supply a NEW task context so they use this job's scope and background-task user.
            // Nothing from the administrator's request is retained.
            var previousContext = httpContextAccessor.HttpContext;
            try
            {
                httpContextAccessor.HttpContext = new DefaultHttpContext
                {
                    RequestServices = scope.ServiceProvider,
                    RequestAborted = stoppingToken
                };
                httpContextAccessor.HttpContext.Request.Path = "/" + NopTaskDefaults.ScheduleTaskPath;
                stoppingToken.ThrowIfCancellationRequested();
                var execution = scope.ServiceProvider.GetRequiredService<IAkeneoProductSyncExecutionService>();
                result = await ExecuteRequestAsync(execution, job.Request, stoppingToken);
            }
            finally
            {
                httpContextAccessor.HttpContext = previousContext;
            }
        }
        catch (Exception exception)
        {
            var canceled = exception is OperationCanceledException;
            var message = canceled
                ? AkeneoCancellationDetails.Describe((OperationCanceledException)exception, stoppingToken, stoppingToken.IsCancellationRequested)
                : $"Akeneo background synchronization failed: {exception.Message}";
            logger.LogError(exception, "Akeneo manual job {JobId}: {Message}", job.Id, message);
            result = new AkeneoProductSyncExecutionResult
            {
                ProfileId = job.Request.ProfileId,
                Canceled = canceled,
                SyncStatus = canceled ? SyncStatus.Cancelled : SyncStatus.Failed,
                Message = message,
                Errors = new List<string> { message }
            };
        }

        lock (_gate)
        {
            _jobs[job.Id] = job with { Result = result };
            _activeJobId = null;
        }
    }

    private static Task<AkeneoProductSyncExecutionResult> ExecuteRequestAsync(
        IAkeneoProductSyncExecutionService execution,
        AkeneoManualSyncRequest request,
        CancellationToken stoppingToken) => request.Kind switch
    {
        AkeneoManualSyncKind.Profile => execution.ImportProductsByProfileAsync(request.ProfileId,
            request.FullSync ? SyncType.ManualFullProfileSync : SyncType.ManualProfileSync, stoppingToken),
        AkeneoManualSyncKind.Resync => execution.ResyncProfileAsync(request.ProfileId, new AkeneoResyncOptions
        {
            RepairDestination = request.RepairDestination,
            UpdateExistingOnly = request.UpdateExistingOnly,
            FamilyCodes = request.FamilyCodes?.ToList() ?? new List<string>()
        }, stoppingToken),
        AkeneoManualSyncKind.Product => execution.SyncProductByUuidAsync(request.ProfileId, request.SourceKey, stoppingToken),
        AkeneoManualSyncKind.ProductModel => execution.SyncProductModelByCodeAsync(request.ProfileId, request.SourceKey, stoppingToken),
        _ => throw new InvalidOperationException("Unknown manual Akeneo sync operation.")
    };
}
