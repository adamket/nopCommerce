using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nop.Core.Infrastructure;
using Nop.Services.Logging;

namespace Apt.Nop.Plugin.Misc.Booster.Services;
public sealed class Debouncer : IDebouncer, IDisposable
{
    private readonly ConcurrentDictionary<object, CancellationTokenSource> _pending = new();
    private readonly object _disposeLock = new();
    private bool _disposed;

    public void Debounce(object key, Func<CancellationToken, Task> action, TimeSpan delay)
    {
        CancellationTokenSource newCts;

        lock (_disposeLock)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(Debouncer));

            newCts = new CancellationTokenSource();

            _pending.AddOrUpdate(
                key,
                newCts,
                (_, existing) =>
                {
                    existing.Cancel();
                    return newCts;
                });
        }

        _ = DebounceAndRunAsync(key, action, newCts, delay);
    }

    private async Task DebounceAndRunAsync(
        object key,
        Func<CancellationToken, Task> action,
        CancellationTokenSource cts,
        TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay, cts.Token);

            if (cts.IsCancellationRequested)
                return;

            await action(cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            await EngineContext.Current.Resolve<ILogger>().ErrorAsync($"Error in debounced action for key '{key}'", ex);
        }
        finally
        {
            if (_pending.TryGetValue(key, out var current) && ReferenceEquals(current, cts))
                _pending.TryRemove(key, out _);

            cts.Dispose();
        }
    }

    public void Dispose()
    {
        CancellationTokenSource[] pending;

        lock (_disposeLock)
        {
            if (_disposed)
                return;

            _disposed = true;
            pending = _pending.Values.ToArray();
            _pending.Clear();
        }

        foreach (var cts in pending)
        {
            cts.Cancel();
            cts.Dispose();
        }
    }
}