using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Nop.Data;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public class AkeneoSyncLogRetentionService(
    IAkeneoSyncProfileService syncProfileService,
    IRepository<AkeneoSyncItemLog> syncItemLogRepository,
    IRepository<AkeneoSyncRunRecord> syncRunRecordRepository,
    IRepository<AkeneoSyncLease> syncLeaseRepository)
    : IAkeneoSyncLogRetentionService
{
    /// <summary>
    /// Rows deleted per statement, so a large backlog never becomes one long
    /// transaction or an oversized IN list.
    /// </summary>
    public const int BatchSize = 1000;

    public async Task<AkeneoSyncLogRetentionResult> TrimAsync(
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var result = new AkeneoSyncLogRetentionResult(0, 0);

        foreach (var profile in await syncProfileService.GetAllAkeneoSyncProfilesAsync())
        {
            if (profile.SyncLogRetentionEnabled)
                result += await TrimProfileAsync(profile, utcNow, cancellationToken);
        }

        return result;
    }

    public async Task<AkeneoSyncLogRetentionResult> TrimProfileAsync(
        AkeneoSyncProfile profile,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var itemLogRetentionDays = Math.Max(1, profile.SyncItemLogRetentionDays);
        var runRecordRetentionDays = Math.Max(1, profile.SyncRunRecordRetentionDays);

        var deletedRuns = await TrimRunRecordsAsync(
            profile.Id,
            utcNow.AddDays(-runRecordRetentionDays),
            cancellationToken);

        var deletedItemLogs = await TrimItemLogsAsync(
            profile.Id,
            utcNow.AddDays(-itemLogRetentionDays),
            cancellationToken);

        return deletedRuns + new AkeneoSyncLogRetentionResult(deletedItemLogs, 0);
    }

    private async Task<int> TrimItemLogsAsync(
        int profileId,
        DateTime cutoffUtc,
        CancellationToken cancellationToken)
    {
        var profileRunIds = syncRunRecordRepository.Table
            .Where(record => record.SyncProfileId == profileId)
            .Select(record => record.Id);

        var deleted = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var ids = await syncItemLogRepository.Table
                .Where(log =>
                    log.CreatedOnUtc < cutoffUtc &&
                    profileRunIds.Contains(log.SyncRunRecordId))
                .OrderBy(log => log.Id)
                .Select(log => log.Id)
                .Take(BatchSize)
                .ToListAsync();

            if (ids.Count == 0)
                return deleted;

            deleted += await syncItemLogRepository.DeleteAsync(log => ids.Contains(log.Id));

            if (ids.Count < BatchSize)
                return deleted;
        }
    }

    private async Task<AkeneoSyncLogRetentionResult> TrimRunRecordsAsync(
        int profileId,
        DateTime cutoffUtc,
        CancellationToken cancellationToken)
    {
        var keptIds = await GetProtectedRunIdsAsync(profileId);
        var deletedItemLogs = 0;
        var deletedRuns = 0;
        var lastId = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = await syncRunRecordRepository.Table
                .Where(record =>
                    record.SyncProfileId == profileId &&
                    record.Id > lastId &&
                    record.StartedOnUtc < cutoffUtc &&
                    record.FinishedOnUtc != null)
                .OrderBy(record => record.Id)
                .Select(record => record.Id)
                .Take(BatchSize)
                .ToListAsync();

            if (batch.Count == 0)
                break;

            lastId = batch[^1];

            var ids = batch.Where(id => !keptIds.Contains(id)).ToList();
            if (ids.Count > 0)
            {
                // Item logs first: a run is never deleted while its logs remain.
                deletedItemLogs += await syncItemLogRepository.DeleteAsync(log => ids.Contains(log.SyncRunRecordId));
                deletedRuns += await syncRunRecordRepository.DeleteAsync(record => ids.Contains(record.Id));
            }

            if (batch.Count < BatchSize)
                break;
        }

        return new AkeneoSyncLogRetentionResult(deletedItemLogs, deletedRuns);
    }

    private async Task<HashSet<int>> GetProtectedRunIdsAsync(int profileId)
    {
        // Same predicate as AkeneoSyncRunRecordService.GetLastSuccessfulRunAsync,
        // which reads the delta watermark.
        var successfulRuns = await syncRunRecordRepository.Table
            .Where(record =>
                record.SyncProfileId == profileId &&
                (record.SyncStatusId == (int)SyncStatus.Completed ||
                 record.SyncStatusId == (int)SyncStatus.CompletedWithWarnings) &&
                (record.RunModeId == (int)AkeneoRunMode.Full ||
                 record.RunModeId == (int)AkeneoRunMode.Delta) &&
                record.CompletedAllPages &&
                !record.WasTruncated)
            .Select(record => new AkeneoRunWatermark(
                record.Id,
                record.ScopeHash,
                record.WatermarkUtc))
            .ToListAsync();

        var keptIds = SelectWatermarkRunIds(successfulRuns);

        var leasedRunIds = await syncLeaseRepository.Table
            .Select(lease => lease.SyncRunRecordId)
            .ToListAsync();

        keptIds.UnionWith(leasedRunIds);

        return keptIds;
    }

    /// <summary>
    /// The runs of one profile that GetLastSuccessfulRunAsync can return: the
    /// latest by watermark (then ID) overall, and for each scope hash.
    /// </summary>
    public static HashSet<int> SelectWatermarkRunIds(IEnumerable<AkeneoRunWatermark> successfulRuns)
    {
        var runs = successfulRuns.ToList();

        static int Latest(IEnumerable<AkeneoRunWatermark> group) => group
            .OrderByDescending(run => run.WatermarkUtc)
            .ThenByDescending(run => run.Id)
            .First()
            .Id;

        var kept = runs
            .Where(run => !string.IsNullOrWhiteSpace(run.ScopeHash))
            .GroupBy(run => run.ScopeHash)
            .Select(Latest)
            .ToHashSet();

        if (runs.Count > 0)
            kept.Add(Latest(runs));

        return kept;
    }
}

public record AkeneoRunWatermark(
    int Id,
    string ScopeHash,
    DateTime WatermarkUtc);
