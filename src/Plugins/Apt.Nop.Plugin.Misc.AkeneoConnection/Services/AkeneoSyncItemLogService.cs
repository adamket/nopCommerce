using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Nop.Core;
using Nop.Data;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public class AkeneoSyncItemLogService(
    IRepository<AkeneoSyncItemLog> syncItemLogRepository)
    : IAkeneoSyncItemLogService
{
    public async Task InsertAkeneoSyncItemLogAsync(AkeneoSyncItemLog syncItem)
    {
        await syncItemLogRepository.InsertAsync(syncItem);
    }

    public async Task UpdateAkeneoSyncItemLogAsync(AkeneoSyncItemLog syncItem)
    {
        await syncItemLogRepository.UpdateAsync(syncItem);
    }

    public async Task DeleteAkeneoSyncItemLogAsync(AkeneoSyncItemLog syncItem)
    {
        await syncItemLogRepository.DeleteAsync(syncItem);
    }

    public async Task<IPagedList<AkeneoSyncItemLog>> SearchAkeneoSyncItemLogsAsync(
        int? syncRunRecordId = null,
        string akeneoProductUuid = null,
        string akeneoIdentifier = null,
        int? nopProductId = null,
        int pageIndex = 0,
        int pageSize = int.MaxValue)
    {
        var query = syncItemLogRepository.Table;

        if (syncRunRecordId.HasValue)
            query = query.Where(log => log.SyncRunRecordId == syncRunRecordId.Value);

        if (!string.IsNullOrWhiteSpace(akeneoProductUuid))
            query = query.Where(log => log.AkeneoProductUuid == akeneoProductUuid);

        if (!string.IsNullOrWhiteSpace(akeneoIdentifier))
            query = query.Where(log => log.AkeneoIdentifier == akeneoIdentifier);

        if (nopProductId.HasValue)
            query = query.Where(log => log.NopProductId == nopProductId.Value);

        return await query
            .OrderByDescending(log => log.CreatedOnUtc)
            .ToPagedListAsync(pageIndex, pageSize);
    }
}