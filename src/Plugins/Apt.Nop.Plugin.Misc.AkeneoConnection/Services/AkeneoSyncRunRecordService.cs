using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Nop.Core;
using Nop.Data;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public class AkeneoSyncRunRecordService(
    IRepository<AkeneoSyncRunRecord> syncRunRecordRepository)
    : IAkeneoSyncRunRecordService
{
    public async Task InsertAkeneoSyncRunRecordAsync(AkeneoSyncRunRecord syncRunRecord) =>
        await syncRunRecordRepository.InsertAsync(syncRunRecord);

    public async Task UpdateAkeneoSyncRunRecordAsync(AkeneoSyncRunRecord syncRunRecord) =>
        await syncRunRecordRepository.UpdateAsync(syncRunRecord);

    public async Task DeleteAkeneoSyncRunRecordAsync(AkeneoSyncRunRecord syncRunRecord) =>
        await syncRunRecordRepository.DeleteAsync(syncRunRecord);

    public async Task<AkeneoSyncRunRecord> GetAkeneoSyncRunRecordByIdAsync(int id) =>
        await syncRunRecordRepository.GetByIdAsync(id);

    public async Task<AkeneoSyncRunRecord> GetActiveRunAsync(int? syncProfileId)
    {
        var query = syncRunRecordRepository.Table.Where(record =>
            record.SyncStatusId == (int)SyncStatus.Started &&
            record.FinishedOnUtc == null);

        if (syncProfileId.HasValue)
            query = query.Where(record => record.SyncProfileId == syncProfileId.Value);

        return await query.OrderByDescending(record => record.Id).FirstOrDefaultAsync();
    }

    public async Task<AkeneoSyncRunRecord> GetLastSuccessfulRunAsync(
        int syncProfileId,
        string scopeHash = null)
    {
        var query = syncRunRecordRepository.Table.Where(record =>
            record.SyncProfileId == syncProfileId &&
            (record.SyncStatusId == (int)SyncStatus.Completed ||
             record.SyncStatusId == (int)SyncStatus.CompletedWithWarnings) &&
            (record.RunModeId == (int)AkeneoRunMode.Full ||
             record.RunModeId == (int)AkeneoRunMode.Delta) &&
            record.CompletedAllPages &&
            !record.WasTruncated);

        if (!string.IsNullOrWhiteSpace(scopeHash))
            query = query.Where(record => record.ScopeHash == scopeHash);

        return await query
            .OrderByDescending(record => record.WatermarkUtc)
            .ThenByDescending(record => record.Id)
            .FirstOrDefaultAsync();
    }

    public async Task<IPagedList<AkeneoSyncRunRecord>> SearchAkeneoSyncRunRecordsAsync(
        DateTime? createdFromUtc = null,
        DateTime? createdToUtc = null,
        int? syncTypeId = null,
        int? syncStatusId = null,
        bool? onlyFailed = null,
        int? syncProfileId = null,
        int? runModeId = null,
        string scopeHash = null,
        int pageIndex = 0,
        int pageSize = int.MaxValue)
    {
        var query = syncRunRecordRepository.Table;

        if (createdFromUtc.HasValue)
            query = query.Where(record => record.StartedOnUtc >= createdFromUtc.Value);

        if (createdToUtc.HasValue)
            query = query.Where(record => record.StartedOnUtc <= createdToUtc.Value);

        if (syncTypeId.HasValue)
            query = query.Where(record => record.SyncTypeId == syncTypeId.Value);

        if (syncStatusId.HasValue)
            query = query.Where(record => record.SyncStatusId == syncStatusId.Value);

        if (onlyFailed == true)
            query = query.Where(record => record.FailedCount > 0);

        if (syncProfileId.HasValue)
            query = query.Where(record => record.SyncProfileId == syncProfileId.Value);

        if (runModeId.HasValue)
            query = query.Where(record => record.RunModeId == runModeId.Value);

        if (!string.IsNullOrWhiteSpace(scopeHash))
            query = query.Where(record => record.ScopeHash == scopeHash);

        query = query.OrderByDescending(record => record.Id);

        return await query.ToPagedListAsync(pageIndex, pageSize);
    }
}
