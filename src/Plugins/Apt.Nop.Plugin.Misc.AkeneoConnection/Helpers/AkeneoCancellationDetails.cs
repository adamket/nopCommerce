using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;

public static class AkeneoCancellationDetails
{
    public static string Describe(OperationCanceledException exception, CancellationToken runToken, bool applicationStopping = false)
    {
        var reason = exception is AkeneoSyncLeaseLostException
            ? "The catalog writer lock was lost."
            : applicationStopping
                ? "The application is shutting down."
                : runToken.IsCancellationRequested
                    ? "The run cancellation token was signaled."
                    : "The run cancellation token was not signaled; an HTTP timeout or another operation may have canceled independently.";

        return $"Product synchronization was canceled. {reason} {exception.GetType().FullName}: {exception.Message}";
    }
}
