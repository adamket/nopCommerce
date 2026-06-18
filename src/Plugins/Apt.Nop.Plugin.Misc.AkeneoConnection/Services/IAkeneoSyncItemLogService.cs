using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Nop.Core;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
public interface IAkeneoSyncItemLogService
{
    Task InsertAkeneoSyncItemLogAsync(AkeneoSyncItemLog entityMapping);
    Task UpdateAkeneoSyncItemLogAsync(AkeneoSyncItemLog entityMapping);
    Task DeleteAkeneoSyncItemLogAsync(AkeneoSyncItemLog entityMapping);

    Task<IPagedList<AkeneoSyncItemLog>> SearchAkeneoSyncItemLogsAsync(
        int? syncRunRecordId = null,
        string akeneoProductUuid = null,
        string akeneoIdentifier = null,
        int? nopProductId = null,
        int pageIndex = 0,
        int pageSize = int.MaxValue);
}
