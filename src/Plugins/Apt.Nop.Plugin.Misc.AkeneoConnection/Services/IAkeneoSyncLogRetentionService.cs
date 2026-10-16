using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public interface IAkeneoSyncLogRetentionService
{
    /// <summary>
    /// Applies <see cref="TrimProfileAsync"/> to every sync profile with
    /// retention enabled. Runs without a profile are never trimmed.
    /// </summary>
    Task<AkeneoSyncLogRetentionResult> TrimAsync(
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the profile's item logs older than its item log retention and
    /// its finished run records (with their item logs) older than its run
    /// record retention. The latest successful run for the profile, and for
    /// each scope, is always kept because delta watermarks are read from it;
    /// unfinished and leased runs are kept too.
    /// </summary>
    Task<AkeneoSyncLogRetentionResult> TrimProfileAsync(
        AkeneoSyncProfile profile,
        DateTime utcNow,
        CancellationToken cancellationToken = default);
}

public record AkeneoSyncLogRetentionResult(
    int DeletedItemLogs,
    int DeletedRunRecords)
{
    public static AkeneoSyncLogRetentionResult operator +(
        AkeneoSyncLogRetentionResult left,
        AkeneoSyncLogRetentionResult right) =>
        new(left.DeletedItemLogs + right.DeletedItemLogs, left.DeletedRunRecords + right.DeletedRunRecords);
}
