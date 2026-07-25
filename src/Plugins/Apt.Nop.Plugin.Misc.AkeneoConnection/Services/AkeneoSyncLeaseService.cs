using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;
using Nop.Data;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public class AkeneoSyncLeaseService(
    IRepository<AkeneoSyncLease> repository)
    : IAkeneoSyncLeaseService
{
    public async Task<AkeneoSyncLease> TryAcquireAsync(
        string lockKey,
        TimeSpan duration)
    {
        if (string.IsNullOrWhiteSpace(lockKey))
            throw new ArgumentException("A sync lease lock key is required.", nameof(lockKey));

        lockKey = lockKey.KeyPart();
        var now = DateTime.UtcNow;

        var existing = await repository.Table.FirstOrDefaultAsync(item =>
            item.LockKey == lockKey);

        if (existing != null)
        {
            if (existing.ExpiresOnUtc > now)
                return null;

            await repository.DeleteAsync(existing);
        }

        var lease = new AkeneoSyncLease
        {
            LockKey = lockKey,
            AcquiredOnUtc = now,
            HeartbeatOnUtc = now,
            ExpiresOnUtc = now.Add(duration)
        };

        try
        {
            // A unique index on LockKey makes this atomic across application instances.
            await repository.InsertAsync(lease);
            return lease;
        }
        catch
        {
            return null;
        }
    }

    public async Task SetRunRecordAsync(
        AkeneoSyncLease lease,
        int syncRunRecordId)
    {
        if (lease == null)
            return;

        lease.SyncRunRecordId = syncRunRecordId;
        lease.HeartbeatOnUtc = DateTime.UtcNow;
        await repository.UpdateAsync(lease);
    }

    public async Task RenewAsync(
        AkeneoSyncLease lease,
        TimeSpan duration)
    {
        if (lease == null)
            return;

        var now = DateTime.UtcNow;
        lease.HeartbeatOnUtc = now;
        lease.ExpiresOnUtc = now.Add(duration);
        await repository.UpdateAsync(lease);
    }

    public async Task RenewByIdAsync(
        int syncLeaseId,
        TimeSpan duration)
    {
        if (syncLeaseId <= 0)
            return;

        var lease = await repository.GetByIdAsync(syncLeaseId);
        if (lease == null)
            return;

        await RenewAsync(lease, duration);
    }

    public async Task ReleaseAsync(AkeneoSyncLease lease)
    {
        if (lease == null)
            return;

        try
        {
            await repository.DeleteAsync(lease);
        }
        catch
        {
            // A completed sync must not be converted to a failure solely because
            // an already-expired lease was cleaned up by another instance.
        }
    }
}
