namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

/// <summary>
/// Thrown at a sync checkpoint when the run's lease no longer exists, which
/// happens when an administrator releases the sync lock manually. Derives from
/// <see cref="OperationCanceledException"/> so the run ends as cancelled.
/// </summary>
public sealed class AkeneoSyncLeaseLostException : OperationCanceledException
{
    public AkeneoSyncLeaseLostException()
        : base("The sync lock was released manually while this run was in progress, so the run stopped at its next checkpoint.")
    {
    }
}
