using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
public interface IAkeneoSyncItemLogService
{
    Task InsertAkeneoSyncItemLogAsync(AkeneoSyncItemLog entityMapping);
    Task UpdateAkeneoSyncItemLogAsync(AkeneoSyncItemLog entityMapping);
    Task DeleteAkeneoSyncItemLogAsync(AkeneoSyncItemLog entityMapping);
}
