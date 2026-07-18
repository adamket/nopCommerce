using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public interface IAkeneoManagedRelationService
{
    Task<AkeneoManagedRelation> UpsertAsync(
        int syncProfileId,
        int syncRunRecordId,
        int nopProductId,
        AkeneoManagedRelationType relationType,
        int nopRelationEntityId,
        string akeneoAttributeCode = null,
        string akeneoValueCode = null);

    Task<IList<AkeneoManagedRelation>> GetByProductAsync(
        int syncProfileId,
        int nopProductId,
        AkeneoManagedRelationType relationType,
        string akeneoAttributeCode = null);

    Task<AkeneoManagedRelation> GetByRelationEntityAsync(
        int syncProfileId,
        AkeneoManagedRelationType relationType,
        int nopRelationEntityId);

    Task DeleteAsync(AkeneoManagedRelation relation);
}
