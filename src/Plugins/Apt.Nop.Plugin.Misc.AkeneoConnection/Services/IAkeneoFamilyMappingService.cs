using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
public interface IAkeneoFamilyMappingService
{
    Task<AkeneoFamilyMapping> GetByIdAsync(int id);

    Task<AkeneoFamilyMapping> GetByFamilyCodeAsync(string akeneoFamilyCode);

    Task<IList<AkeneoFamilyMapping>> GetAllAsync();

    Task<IList<AkeneoFamilyVariantAxisMapping>> GetAxisMappingsAsync(int configurationId);

    Task InsertAsync(AkeneoFamilyMapping configuration);

    Task UpdateAsync(AkeneoFamilyMapping configuration);

    Task DeleteAsync(AkeneoFamilyMapping configuration);
    Task InsertAxisMappingAsync(AkeneoFamilyVariantAxisMapping mapping);

    Task UpdateAxisMappingAsync(AkeneoFamilyVariantAxisMapping mapping);

    Task DeleteAxisMappingAsync(AkeneoFamilyVariantAxisMapping mapping);

    Task<AkeneoVariantRelationshipOptions> BuildOptionsForFamilyAsync(string akeneoFamilyCode);
}
