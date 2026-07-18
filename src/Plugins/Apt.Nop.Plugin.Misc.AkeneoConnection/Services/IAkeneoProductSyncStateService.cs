using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public interface IAkeneoProductSyncStateService
{
    Task<AkeneoProductSyncState> GetBySourceAsync(
        int syncProfileId,
        AkeneoEntityType akeneoEntityType,
        string akeneoCode,
        string akeneoUuid);

    Task<AkeneoProductSyncState> MarkSeenAsync(
        int syncProfileId,
        int syncRunRecordId,
        AkeneoEntityType akeneoEntityType,
        string akeneoCode,
        string akeneoUuid,
        string akeneoParentCode,
        AkeneoProductImportResult result,
        string desiredStateHash = null);

    Task<IList<AkeneoProductSyncState>> GetUnseenStatesAsync(
        int syncProfileId,
        int currentRunRecordId);

    Task UpdateLifecycleStatusAsync(
        AkeneoProductSyncState state,
        AkeneoProductLifecycleStatus lifecycleStatus);

    Task DeleteAsync(AkeneoProductSyncState state);
}
