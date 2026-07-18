using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Nop.Core;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public interface IAkeneoSyncRunRecordService
{
    Task InsertAkeneoSyncRunRecordAsync(AkeneoSyncRunRecord syncRunRecord);
    Task UpdateAkeneoSyncRunRecordAsync(AkeneoSyncRunRecord syncRunRecord);
    Task DeleteAkeneoSyncRunRecordAsync(AkeneoSyncRunRecord syncRunRecord);

    Task<AkeneoSyncRunRecord> GetAkeneoSyncRunRecordByIdAsync(int id);

    Task<AkeneoSyncRunRecord> GetActiveRunAsync(int? syncProfileId);

    Task<AkeneoSyncRunRecord> GetLastSuccessfulRunAsync(
        int syncProfileId,
        string scopeHash = null);

    Task<IPagedList<AkeneoSyncRunRecord>> SearchAkeneoSyncRunRecordsAsync(
        DateTime? createdFromUtc = null,
        DateTime? createdToUtc = null,
        int? syncTypeId = null,
        int? syncStatusId = null,
        bool? onlyFailed = null,
        int? syncProfileId = null,
        int? runModeId = null,
        string scopeHash = null,
        int pageIndex = 0,
        int pageSize = int.MaxValue);
}
