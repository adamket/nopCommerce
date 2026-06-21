using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
public interface IAkeneoFamilyVariantImportConfigurationService
{
    Task<AkeneoFamilyVariantImportConfiguration> GetByIdAsync(int id);

    Task<AkeneoFamilyVariantImportConfiguration> GetByFamilyCodeAsync(string akeneoFamilyCode);

    Task<IList<AkeneoFamilyVariantImportConfiguration>> GetAllAsync();

    Task<IList<AkeneoFamilyVariantAxisMapping>> GetAxisMappingsAsync(int configurationId);

    Task InsertAsync(AkeneoFamilyVariantImportConfiguration configuration);

    Task UpdateAsync(AkeneoFamilyVariantImportConfiguration configuration);

    Task DeleteAsync(AkeneoFamilyVariantImportConfiguration configuration);

    Task InsertAxisMappingAsync(AkeneoFamilyVariantAxisMapping mapping);

    Task UpdateAxisMappingAsync(AkeneoFamilyVariantAxisMapping mapping);

    Task DeleteAxisMappingAsync(AkeneoFamilyVariantAxisMapping mapping);

    Task<AkeneoVariantRelationshipOptions> BuildOptionsForFamilyAsync(string akeneoFamilyCode);
}
