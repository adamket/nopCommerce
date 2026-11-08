using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public interface IAkeneoSyncLeaseService
{
    Task<AkeneoSyncLease> TryAcquireAsync(
        string lockKey,
        TimeSpan duration);

    Task SetRunRecordAsync(
        AkeneoSyncLease lease,
        int syncRunRecordId);

    /// <summary>
    /// Extends a held lease. Returns false when the lease no longer exists
    /// (it expired and was taken over, or was released manually), so the
    /// holder must stop.
    /// </summary>
    Task<bool> RenewAsync(
        AkeneoSyncLease lease,
        TimeSpan duration);

    /// <summary>
    /// Extends a held lease by ID. Returns false when the lease no longer
    /// exists, so the holder must stop.
    /// </summary>
    Task<bool> RenewByIdAsync(
        int syncLeaseId,
        TimeSpan duration);

    /// <summary>
    /// Extends <paramref name="lease"/> when its last check-in is older than the
    /// renewal interval, or always when <paramref name="force"/> is set. Throws
    /// <see cref="AkeneoSyncLeaseLostException"/> when the lease is no longer
    /// held, so the caller stops before writing anything else.
    /// </summary>
    Task RenewIfDueAsync(
        AkeneoSyncLease lease,
        TimeSpan duration,
        bool force = false);

    Task ReleaseAsync(AkeneoSyncLease lease);

    /// <summary>
    /// The lease row for <paramref name="lockKey"/>, expired or not, or null.
    /// </summary>
    Task<AkeneoSyncLease> GetLeaseAsync(string lockKey);

    /// <summary>
    /// Deletes the lease for <paramref name="lockKey"/> only when it has expired
    /// or its holder has not checked in for <paramref name="staleAfter"/>, so a
    /// run that is still working is never overlapped. Returns the removed
    /// lease, or null when none was removed.
    /// </summary>
    Task<AkeneoSyncLease> ForceReleaseAsync(
        string lockKey,
        TimeSpan staleAfter);
}
