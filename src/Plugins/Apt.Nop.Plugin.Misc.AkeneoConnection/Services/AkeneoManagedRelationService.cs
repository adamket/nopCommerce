using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Nop.Data;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public class AkeneoManagedRelationService(
    IRepository<AkeneoManagedRelation> repository)
    : IAkeneoManagedRelationService
{
    public async Task<AkeneoManagedRelation> UpsertAsync(
        int syncProfileId,
        int syncRunRecordId,
        int nopProductId,
        AkeneoManagedRelationType relationType,
        int nopRelationEntityId,
        string akeneoAttributeCode = null,
        string akeneoValueCode = null)
    {
        if (syncProfileId <= 0 || nopProductId <= 0 || nopRelationEntityId <= 0)
            return null;

        var relation = await repository.Table.FirstOrDefaultAsync(item =>
            item.SyncProfileId == syncProfileId &&
            item.RelationTypeId == (int)relationType &&
            item.NopRelationEntityId == nopRelationEntityId);

        var now = DateTime.UtcNow;

        if (relation == null)
        {
            relation = new AkeneoManagedRelation
            {
                SyncProfileId = syncProfileId,
                NopProductId = nopProductId,
                RelationTypeId = (int)relationType,
                NopRelationEntityId = nopRelationEntityId,
                AkeneoAttributeCode = akeneoAttributeCode?.Trim(),
                AkeneoValueCode = akeneoValueCode?.Trim(),
                LastSeenRunRecordId = syncRunRecordId,
                CreatedOnUtc = now,
                UpdatedOnUtc = now
            };

            await repository.InsertAsync(relation);
            return relation;
        }

        relation.NopProductId = nopProductId;
        relation.AkeneoAttributeCode = akeneoAttributeCode?.Trim();
        relation.AkeneoValueCode = akeneoValueCode?.Trim();
        relation.LastSeenRunRecordId = syncRunRecordId;
        relation.UpdatedOnUtc = now;

        await repository.UpdateAsync(relation);
        return relation;
    }

    public async Task<IList<AkeneoManagedRelation>> GetByProductAsync(
        int syncProfileId,
        int nopProductId,
        AkeneoManagedRelationType relationType,
        string akeneoAttributeCode = null)
    {
        var query = repository.Table.Where(item =>
            item.SyncProfileId == syncProfileId &&
            item.NopProductId == nopProductId &&
            item.RelationTypeId == (int)relationType);

        if (!string.IsNullOrWhiteSpace(akeneoAttributeCode))
        {
            var normalized = akeneoAttributeCode.Trim();
            query = query.Where(item => item.AkeneoAttributeCode == normalized);
        }

        return await query.ToListAsync();
    }

    public async Task<AkeneoManagedRelation> GetByRelationEntityAsync(
        int syncProfileId,
        AkeneoManagedRelationType relationType,
        int nopRelationEntityId)
    {
        return await repository.Table.FirstOrDefaultAsync(item =>
            item.SyncProfileId == syncProfileId &&
            item.RelationTypeId == (int)relationType &&
            item.NopRelationEntityId == nopRelationEntityId);
    }

    public async Task DeleteAsync(AkeneoManagedRelation relation)
    {
        if (relation != null)
            await repository.DeleteAsync(relation);
    }
}
