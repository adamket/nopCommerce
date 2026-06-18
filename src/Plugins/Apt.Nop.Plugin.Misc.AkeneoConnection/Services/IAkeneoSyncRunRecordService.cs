using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Nop.Core;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
public interface IAkeneoSyncRunRecordService
{
    Task InsertAkeneoSyncRunRecordAsync(AkeneoSyncRunRecord syncRunRecord);
    Task UpdateAkeneoSyncRunRecordAsync(AkeneoSyncRunRecord syncRunRecord);
    Task DeleteAkeneoSyncRunRecordAsync(AkeneoSyncRunRecord syncRunRecord);

    Task<AkeneoSyncRunRecord> GetAkeneoSyncRunRecordByIdAsync(int id);

    Task<IPagedList<AkeneoSyncRunRecord>> SearchAkeneoSyncRunRecordsAsync(
        DateTime? createdFromUtc = null,
        DateTime? createdToUtc = null,
        int? syncTypeId = null,
        int? syncStatusId = null,
        bool? onlyFailed = null,
        int pageIndex = 0,
        int pageSize = int.MaxValue);
}
 