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

    Task RenewByIdAsync(
        int syncLeaseId,
        TimeSpan duration);

    Task ReleaseAsync(AkeneoSyncLease lease);
}
