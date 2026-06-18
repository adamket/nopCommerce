using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Nop.Core;
using Nop.Data;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
public class AkeneoSyncRunRecordService(IRepository<AkeneoSyncRunRecord> syncRunRecordRepository)
    : IAkeneoSyncRunRecordService
{

    public async Task InsertAkeneoSyncRunRecordAsync(AkeneoSyncRunRecord syncRunRecord)
    {
        await syncRunRecordRepository.InsertAsync(syncRunRecord);
    }

    public async Task UpdateAkeneoSyncRunRecordAsync(AkeneoSyncRunRecord syncRunRecord)
    {
        await syncRunRecordRepository.UpdateAsync(syncRunRecord);
    }

    public async Task DeleteAkeneoSyncRunRecordAsync(AkeneoSyncRunRecord syncRunRecord)
    {
        await syncRunRecordRepository.DeleteAsync(syncRunRecord);
    }

    public async Task<AkeneoSyncRunRecord> GetAkeneoSyncRunRecordByIdAsync(int id)
    {
        var item = syncRunRecordRepository.GetById(id);
        return item;
    }

    public async Task<IPagedList<AkeneoSyncRunRecord>> SearchAkeneoSyncRunRecordsAsync(DateTime? createdFromUtc = null, DateTime? createdToUtc = null,
        int? syncTypeId = null, int? syncStatusId = null, bool? onlyFailed = null, int pageIndex = 0,
        int pageSize = int.MaxValue)
    {
        var query = syncRunRecordRepository.Table;

        if (createdFromUtc.HasValue)
            query = query.Where(q => q.StartedOnUtc >= createdFromUtc.Value);   

        if (createdToUtc.HasValue)
            query = query.Where(q => q.StartedOnUtc <= createdToUtc.Value);

        if (syncTypeId.HasValue)
        {
            query = query.Where(q => q.SyncTypeId == syncTypeId.Value);
        }

        if (syncStatusId.HasValue)
        {
            query = query.Where(q => q.SyncStatusId == syncStatusId.Value);
        }

        if (onlyFailed.HasValue && onlyFailed.Value)
        {
            query = query.Where(q => q.FailedCount > 0);
        }

        query = query.OrderByDescending(q => q.Id);

        return await query.ToPagedListAsync(pageIndex, pageSize);


    }
}
