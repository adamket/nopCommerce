using Apt.Nop.Plugin.AkeneoConnection.Domain;

namespace Apt.Nop.Plugin.AkeneoConnection.Services;
public interface IAkeneoSyncItemLogService
{
    Task InsertAkeneoSyncItemLogAsync(AkeneoSyncItemLog entityMapping);
    Task UpdateAkeneoSyncItemLogAsync(AkeneoSyncItemLog entityMapping);
    Task DeleteAkeneoSyncItemLogAsync(AkeneoSyncItemLog entityMapping);
}
