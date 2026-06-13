using Apt.Nop.Plugin.AkeneoConnection.Domain;
using Nop.Data;

namespace Apt.Nop.Plugin.AkeneoConnection.Services;
public class AkeneoSyncItemLogService(IRepository<AkeneoSyncItemLog> syncItemLogRepository)
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
}
