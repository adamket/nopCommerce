using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Nop.Data;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public class AkeneoProductSyncStateService(
    IRepository<AkeneoProductSyncState> repository)
    : IAkeneoProductSyncStateService
{
    public async Task<AkeneoProductSyncState> GetBySourceAsync(
        int syncProfileId,
        AkeneoEntityType akeneoEntityType,
        string akeneoCode,
        string akeneoUuid)
    {
        if (syncProfileId <= 0)
            return null;

        akeneoCode = akeneoCode?.Trim();
        akeneoUuid = akeneoUuid?.Trim();

        var query = repository.Table.Where(state =>
            state.SyncProfileId == syncProfileId &&
            state.AkeneoEntityTypeId == (int)akeneoEntityType);

        if (!string.IsNullOrWhiteSpace(akeneoUuid))
        {
            var byUuid = await query.FirstOrDefaultAsync(state =>
                state.AkeneoUuid == akeneoUuid);

            if (byUuid != null)
                return byUuid;
        }

        if (string.IsNullOrWhiteSpace(akeneoCode))
            return null;

        return await query.FirstOrDefaultAsync(state =>
            state.AkeneoCode == akeneoCode);
    }

    public async Task<AkeneoProductSyncState> MarkSeenAsync(
        int syncProfileId,
        int syncRunRecordId,
        AkeneoEntityType akeneoEntityType,
        string akeneoCode,
        string akeneoUuid,
        string akeneoParentCode,
        AkeneoProductImportResult result,
        string desiredStateHash = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        var state = await GetBySourceAsync(
            syncProfileId,
            akeneoEntityType,
            akeneoCode,
            akeneoUuid);

        var now = DateTime.UtcNow;

        if (state == null)
        {
            state = new AkeneoProductSyncState
            {
                SyncProfileId = syncProfileId,
                AkeneoEntityTypeId = (int)akeneoEntityType,
                AkeneoCode = akeneoCode?.Trim(),
                AkeneoUuid = akeneoUuid?.Trim(),
                AkeneoParentCode = akeneoParentCode?.Trim(),
                DestinationKindId = (int)result.DestinationKind,
                NopProductId = result.NopProductId,
                NopParentProductId = result.NopParentProductId,
                NopProductAttributeCombinationId = result.NopProductAttributeCombinationId,
                NopProductAttributeValueId = result.NopProductAttributeValueId,
                LastSeenRunRecordId = syncRunRecordId,
                LastSeenOnUtc = now,
                LastDesiredStateHash = desiredStateHash,
                LifecycleStatusId = (int)AkeneoProductLifecycleStatus.Active,
                CreatedOnUtc = now,
                UpdatedOnUtc = now
            };

            await repository.InsertAsync(state);
            return state;
        }

        var changed = false;

        changed |= AkeneoMappingHelper.SetIfChanged(
            state.AkeneoCode,
            akeneoCode?.Trim(),
            value => state.AkeneoCode = value);

        changed |= AkeneoMappingHelper.SetIfChanged(
            state.AkeneoUuid,
            akeneoUuid?.Trim(),
            value => state.AkeneoUuid = value);

        changed |= AkeneoMappingHelper.SetIfChanged(
            state.AkeneoParentCode,
            akeneoParentCode?.Trim(),
            value => state.AkeneoParentCode = value);

        changed |= AkeneoMappingHelper.SetIfChanged(
            state.DestinationKindId,
            (int)result.DestinationKind,
            value => state.DestinationKindId = value);

        changed |= AkeneoMappingHelper.SetIfChanged(
            state.NopProductId,
            result.NopProductId,
            value => state.NopProductId = value);

        changed |= AkeneoMappingHelper.SetIfChanged(
            state.NopParentProductId,
            result.NopParentProductId,
            value => state.NopParentProductId = value);

        changed |= AkeneoMappingHelper.SetIfChanged(
            state.NopProductAttributeCombinationId,
            result.NopProductAttributeCombinationId,
            value => state.NopProductAttributeCombinationId = value);

        changed |= AkeneoMappingHelper.SetIfChanged(
            state.NopProductAttributeValueId,
            result.NopProductAttributeValueId,
            value => state.NopProductAttributeValueId = value);

        changed |= AkeneoMappingHelper.SetIfChanged(
            state.LastDesiredStateHash,
            desiredStateHash,
            value => state.LastDesiredStateHash = value);

        changed |= AkeneoMappingHelper.SetIfChanged(
            state.LifecycleStatusId,
            (int)AkeneoProductLifecycleStatus.Active,
            value => state.LifecycleStatusId = value);

        state.LastSeenRunRecordId = syncRunRecordId;
        state.LastSeenOnUtc = now;
        state.UpdatedOnUtc = now;

        // Last-seen values always change per authoritative run, so persist even
        // when the destination binding itself is unchanged.
        await repository.UpdateAsync(state);

        return state;
    }

    public async Task<IList<AkeneoProductSyncState>> GetUnseenStatesAsync(
        int syncProfileId,
        int currentRunRecordId)
    {
        return await repository.Table
            .Where(state =>
                state.SyncProfileId == syncProfileId &&
                state.LastSeenRunRecordId != currentRunRecordId &&
                state.LifecycleStatusId == (int)AkeneoProductLifecycleStatus.Active)
            .ToListAsync();
    }

    public async Task UpdateLifecycleStatusAsync(
        AkeneoProductSyncState state,
        AkeneoProductLifecycleStatus lifecycleStatus)
    {
        ArgumentNullException.ThrowIfNull(state);

        state.LifecycleStatusId = (int)lifecycleStatus;
        state.UpdatedOnUtc = DateTime.UtcNow;
        await repository.UpdateAsync(state);
    }

    public async Task DeleteAsync(AkeneoProductSyncState state)
    {
        if (state != null)
            await repository.DeleteAsync(state);
    }
}
