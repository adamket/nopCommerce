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

    public async Task<IReadOnlyDictionary<int, int>> GetActionCountsAsync(int syncRunRecordId)
    {
        var logs = WithEffectiveActions(syncItemLogRepository.Table.Where(log => log.SyncRunRecordId == syncRunRecordId));
        var counts = await logs.GroupBy(log => log.ActionTypeId)
            .Select(group => new { Action = group.Key, Count = group.Count() })
            .ToListAsync();
        return counts.ToDictionary(group => group.Action, group => group.Count);
    }

    private static IQueryable<AkeneoSyncItemLog> WithEffectiveActions(IQueryable<AkeneoSyncItemLog> query) =>
        query.Select(log => new AkeneoSyncItemLog
        {
            Id = log.Id,
            SyncRunRecordId = log.SyncRunRecordId,
            AkeneoProductUuid = log.AkeneoProductUuid,
            AkeneoIdentifier = log.AkeneoIdentifier,
            NopProductId = log.NopProductId,
            CreatedOnUtc = log.CreatedOnUtc,
            Message = log.Message,
            ActionTypeId = log.ActionTypeId == (int)SyncItemActionType.Skipped && log.NopProductId == 0 &&
                log.AkeneoIdentifier == "Run complete" && log.Message != null && log.Message.StartsWith("Run complete (")
                ? (int)SyncItemActionType.RunComplete : log.ActionTypeId
        });

    public async Task<IPagedList<AkeneoSyncItemLog>> SearchAkeneoSyncItemLogsAsync(
        int? syncRunRecordId = null,
        string akeneoProductUuid = null,
        string akeneoIdentifier = null,
        int? nopProductId = null,
        int? actionTypeId = null,
        int pageIndex = 0,
        int pageSize = int.MaxValue,
        IReadOnlyCollection<int> actionTypeIds = null)
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

        query = WithEffectiveActions(query);

        if (actionTypeId.HasValue)
            query = query.Where(log => log.ActionTypeId == actionTypeId.Value);

        if (actionTypeIds is { Count: > 0 })
            query = query.Where(log => actionTypeIds.Contains(log.ActionTypeId));

        return await query
            .OrderByDescending(log => log.CreatedOnUtc)
            .ToPagedListAsync(pageIndex, pageSize);
    }
}