using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;
using Nop.Data;
using Nop.Services.Logging;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public class AkeneoSyncLeaseService(
    IRepository<AkeneoSyncLease> repository,
    ILogger logger)
    : IAkeneoSyncLeaseService
{
    /// <summary>
    /// How often <see cref="RenewIfDueAsync"/> checks in. Every lease duration
    /// must comfortably exceed this plus the longest step between checkpoints.
    /// </summary>
    public static readonly TimeSpan RenewalInterval = TimeSpan.FromMinutes(5);

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

            // Delete the expired lease only if it is still expired: a holder that
            // renewed it since the read above keeps it.
            var existingId = existing.Id;
            var removed = await repository.DeleteAsync(item =>
                item.Id == existingId && item.ExpiresOnUtc <= now);

            if (removed == 0)
                return null;
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
            await repository.InsertAsync(lease);
            return lease;
        }
        catch (Exception ex)
        {
            // Only a unique-index collision means another instance holds the lease.
            // Confirm that's the case; otherwise this is a real failure worth surfacing.
            var held = await repository.Table.AnyAsync(l =>
                l.LockKey == lease.LockKey && l.ExpiresOnUtc > DateTime.UtcNow);
            if (held)
                return null;

            await logger.ErrorAsync("Akeneo sync lease acquisition failed.", ex);
            throw;
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

    public async Task<bool> RenewAsync(
        AkeneoSyncLease lease,
        TimeSpan duration)
    {
        if (lease == null)
            return true;

        var now = DateTime.UtcNow;
        lease.HeartbeatOnUtc = now;
        lease.ExpiresOnUtc = now.Add(duration);
        await repository.UpdateAsync(lease);

        // Updating a row that was taken over or released matches nothing and
        // reports no error, so confirm the row is still there. A takeover only
        // deletes a lease that is still expired, so once this update lands no
        // other instance can take it; if the row is gone, this run lost it.
        var leaseId = lease.Id;
        return await repository.Table.AnyAsync(item => item.Id == leaseId);
    }

    public async Task<bool> RenewByIdAsync(
        int syncLeaseId,
        TimeSpan duration)
    {
        if (syncLeaseId <= 0)
            return true;

        var lease = await repository.GetByIdAsync(syncLeaseId);
        if (lease == null)
            return false;

        return await RenewAsync(lease, duration);
    }

    public async Task RenewIfDueAsync(
        AkeneoSyncLease lease,
        TimeSpan duration,
        bool force = false)
    {
        if (lease == null)
            return;

        if (!force && DateTime.UtcNow - lease.HeartbeatOnUtc < RenewalInterval)
            return;

        if (!await RenewAsync(lease, duration))
            throw new AkeneoSyncLeaseLostException();
    }

    public async Task<AkeneoSyncLease> GetLeaseAsync(string lockKey)
    {
        if (string.IsNullOrWhiteSpace(lockKey))
            return null;

        lockKey = lockKey.KeyPart();

        return await repository.Table.FirstOrDefaultAsync(item =>
            item.LockKey == lockKey);
    }

    public async Task<AkeneoSyncLease> ForceReleaseAsync(
        string lockKey,
        TimeSpan staleAfter)
    {
        var lease = await GetLeaseAsync(lockKey);
        if (lease == null)
            return null;

        // Only a lease whose holder has stopped checking in may be removed, so a
        // run that is still working can never overlap the next one. The heartbeat
        // is re-checked in the delete in case the holder renewed meanwhile.
        var leaseId = lease.Id;
        var staleBeforeUtc = DateTime.UtcNow - staleAfter;
        var removed = await repository.DeleteAsync(item =>
            item.Id == leaseId &&
            (item.HeartbeatOnUtc <= staleBeforeUtc || item.ExpiresOnUtc <= DateTime.UtcNow));

        if (removed == 0)
            return null;

        await logger.WarningAsync(
            $"Akeneo sync lock '{lease.LockKey}' (run record {lease.SyncRunRecordId}, " +
            $"acquired {lease.AcquiredOnUtc:u}, last heartbeat {lease.HeartbeatOnUtc:u}) was released manually.");

        return lease;
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
