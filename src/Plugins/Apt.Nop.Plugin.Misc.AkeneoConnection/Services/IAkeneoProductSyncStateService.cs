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

    /// <param name="knownState">
    /// The state row the caller already loaded for this source in this item, if
    /// any. It is used instead of a new lookup when it matches the source.
    /// </param>
    Task<AkeneoProductSyncState> MarkSeenAsync(
        int syncProfileId,
        int syncRunRecordId,
        AkeneoEntityType akeneoEntityType,
        string akeneoCode,
        string akeneoUuid,
        string akeneoParentCode,
        AkeneoProductImportResult result,
        string desiredStateHash = null,
        AkeneoProductSyncState knownState = null);

    /// <summary>Update only encounter timestamps; preserve binding, hash, and lifecycle.</summary>
    Task MarkSkippedSeenAsync(AkeneoProductSyncState state, int syncRunRecordId);

    Task<IList<AkeneoProductSyncState>> GetUnseenStatesAsync(
        int syncProfileId,
        int currentRunRecordId);

    Task UpdateLifecycleStatusAsync(
        AkeneoProductSyncState state,
        AkeneoProductLifecycleStatus lifecycleStatus);

    Task DeleteAsync(AkeneoProductSyncState state);
}
