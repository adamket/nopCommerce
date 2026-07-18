// Services/Http/AkeneoTransientRetryHandler.cs
using System.Net;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services.Http;

public sealed class AkeneoTransientRetryHandler : DelegatingHandler
{
    private const int MaxAttempts = 4;                 // 1 try + 3 retries
    private static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan PerAttemptTimeout = TimeSpan.FromSeconds(30);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptCts.CancelAfter(PerAttemptTimeout);

            HttpResponseMessage response = null;
            var isTransient = false;
            TimeSpan? retryAfter = null;

            try
            {
                response = await base.SendAsync(request, attemptCts.Token);

                if (!IsTransientStatus(response.StatusCode))
                    return response;

                isTransient = true;
                retryAfter = response.Headers.RetryAfter?.Delta
                    ?? (response.Headers.RetryAfter?.Date is { } date
                        ? date - DateTimeOffset.UtcNow
                        : null);
            }
            catch (HttpRequestException) when (attempt < MaxAttempts)
            {
                isTransient = true;
            }
            catch (OperationCanceledException) when (
                !cancellationToken.IsCancellationRequested && attempt < MaxAttempts)
            {
                // Per-attempt timeout fired (not caller cancellation) — treat as transient.
                isTransient = true;
            }

            if (!isTransient || attempt >= MaxAttempts)
            {
                // Out of retries: hand back the last response (caller's EnsureSuccessAsync
                // surfaces it) or, if we only ever threw, let the final exception escape
                // by making one last un-caught attempt.
                if (response != null)
                    return response;

                return await base.SendAsync(request, cancellationToken);
            }

            response?.Dispose();

            var delay = retryAfter is { } ra && ra > TimeSpan.Zero
                ? ra
                : ComputeBackoff(attempt);

            await Task.Delay(delay, cancellationToken);
        }
    }

    private static bool IsTransientStatus(HttpStatusCode status) =>
        status == HttpStatusCode.RequestTimeout ||          // 408
        status == (HttpStatusCode)429 ||                    // TooManyRequests
        status == HttpStatusCode.BadGateway ||              // 502
        status == HttpStatusCode.ServiceUnavailable ||      // 503
        status == HttpStatusCode.GatewayTimeout;            // 504

    private static TimeSpan ComputeBackoff(int attempt)
    {
        var exponential = BaseDelay * Math.Pow(2, attempt - 1);
        var jitterMs = Random.Shared.Next(0, 250);
        return exponential + TimeSpan.FromMilliseconds(jitterMs);
    }
}