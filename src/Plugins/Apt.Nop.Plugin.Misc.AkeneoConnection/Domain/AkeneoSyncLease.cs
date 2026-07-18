using Nop.Core;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;

/// <summary>
/// Database-backed lease used to prevent concurrent writers for the same
/// synchronization scope across multiple web application instances.
/// </summary>
public class AkeneoSyncLease : BaseEntity
{
    public string LockKey { get; set; }

    public int SyncRunRecordId { get; set; }

    public DateTime AcquiredOnUtc { get; set; }

    public DateTime HeartbeatOnUtc { get; set; }

    public DateTime ExpiresOnUtc { get; set; }
}
