using Microsoft.Extensions.DependencyInjection;
using Nop.Services.Logging;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

/// <summary>A stop request is a run-history entry shared by all web instances. Poll it
/// independently of the sync transaction, including while the worker awaits Akeneo.</summary>
public sealed class AkeneoRunStopMonitor : IAsyncDisposable
{
    private readonly CancellationTokenSource _work;
    private readonly CancellationTokenSource _finished = new();
    private readonly Task _poll;
    public CancellationToken Token => _work.Token;
    public string Reason { get; private set; }

    public AkeneoRunStopMonitor(IServiceScopeFactory scopes, int runId, CancellationToken caller)
    {
        _work = CancellationTokenSource.CreateLinkedTokenSource(caller);
        _poll = scopes == null ? Task.CompletedTask : PollAsync(scopes, runId);
    }

    private async Task PollAsync(IServiceScopeFactory scopes, int runId)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        var reportedFailure = false;
        try
        {
            while (await timer.WaitForNextTickAsync(_finished.Token))
            {
                using var scope = scopes.CreateScope();
                string request;
                try
                {
                    var logs = scope.ServiceProvider.GetRequiredService<IAkeneoSyncItemLogService>();
                    request = await logs.GetStopRequestAsync(runId);
                    reportedFailure = false;
                }
                catch (Exception ex)
                {
                    if (!reportedFailure)
                        await scope.ServiceProvider.GetRequiredService<ILogger>().WarningAsync(
                            $"Could not check stop requests for Akeneo run {runId}. Will retry.", ex);
                    reportedFailure = true;
                    continue;
                }

                if (request == null) continue;
                Reason = request;
                _work.Cancel();
                return;
            }
        }
        catch (OperationCanceledException) when (_finished.IsCancellationRequested) { }
    }

    public async ValueTask DisposeAsync()
    {
        _finished.Cancel();
        try { await _poll; }
        finally
        {
            _work.Dispose();
            _finished.Dispose();
        }
    }
}
