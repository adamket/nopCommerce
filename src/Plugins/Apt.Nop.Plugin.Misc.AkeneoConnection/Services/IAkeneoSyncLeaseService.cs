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

    Task RenewAsync(
        AkeneoSyncLease lease,
        TimeSpan duration);

    /// <summary>
    /// Extends a held lease. Returns false when the lease no longer exists
    /// (for example it was released manually), so the holder must stop.
    /// </summary>
    Task<bool> RenewByIdAsync(
        int syncLeaseId,
        TimeSpan duration);

    Task ReleaseAsync(AkeneoSyncLease lease);

    /// <summary>
    /// The lease row for <paramref name="lockKey"/>, expired or not, or null.
    /// </summary>
    Task<AkeneoSyncLease> GetLeaseAsync(string lockKey);

    /// <summary>
    /// Deletes the lease for <paramref name="lockKey"/> regardless of its
    /// holder. Returns the removed lease, or null when none was held.
    /// </summary>
    Task<AkeneoSyncLease> ForceReleaseAsync(string lockKey);
}
