using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Nop.Data;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public class AkeneoFamilyVariantImportConfigurationService(
    IRepository<AkeneoFamilyVariantImportConfiguration> configurationRepository,
    IRepository<AkeneoFamilyVariantAxisMapping> axisMappingRepository)
    : IAkeneoFamilyVariantImportConfigurationService
{
    public async Task<AkeneoFamilyVariantImportConfiguration> GetByIdAsync(int id)
    {
        return await configurationRepository.GetByIdAsync(id);
    }

    public async Task<AkeneoFamilyVariantImportConfiguration> GetByFamilyCodeAsync(string akeneoFamilyCode)
    {
        if (string.IsNullOrWhiteSpace(akeneoFamilyCode))
            return null;

        akeneoFamilyCode = akeneoFamilyCode.Trim();

        return await configurationRepository.Table
            .FirstOrDefaultAsync(x => x.AkeneoFamilyCode == akeneoFamilyCode);
    }

    public async Task<IList<AkeneoFamilyVariantImportConfiguration>> GetAllAsync()
    {
        return await configurationRepository.Table
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.AkeneoFamilyCode)
            .ToListAsync();
    }

    public async Task<IList<AkeneoFamilyVariantAxisMapping>> GetAxisMappingsAsync(int configurationId)
    {
        return await axisMappingRepository.Table
            .Where(x => x.FamilyVariantImportConfigurationId == configurationId)
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Id)
            .ToListAsync();
    }

    public async Task InsertAsync(AkeneoFamilyVariantImportConfiguration configuration)
    {
        configuration.CreatedOnUtc = DateTime.UtcNow;
        configuration.UpdatedOnUtc = DateTime.UtcNow;

        await configurationRepository.InsertAsync(configuration);
    }

    public async Task UpdateAsync(AkeneoFamilyVariantImportConfiguration configuration)
    {
        configuration.UpdatedOnUtc = DateTime.UtcNow;

        await configurationRepository.UpdateAsync(configuration);
    }

    public async Task DeleteAsync(AkeneoFamilyVariantImportConfiguration configuration)
    {
        await configurationRepository.DeleteAsync(configuration);
    }

    public async Task InsertAxisMappingAsync(AkeneoFamilyVariantAxisMapping mapping)
    {
        await axisMappingRepository.InsertAsync(mapping);
    }

    public async Task UpdateAxisMappingAsync(AkeneoFamilyVariantAxisMapping mapping)
    {
        await axisMappingRepository.UpdateAsync(mapping);
    }

    public async Task DeleteAxisMappingAsync(AkeneoFamilyVariantAxisMapping mapping)
    {
        await axisMappingRepository.DeleteAsync(mapping);
    }

    public async Task<AkeneoVariantRelationshipOptions> BuildOptionsForFamilyAsync(string akeneoFamilyCode)
    {
        var configuration = await GetByFamilyCodeAsync(akeneoFamilyCode);

        if (configuration == null || !configuration.Enabled)
            return null;

        var axisMappings = await GetAxisMappingsAsync(configuration.Id);

        return new AkeneoVariantRelationshipOptions
        {
            FamilyVariantImportConfigurationId = configuration.Id,
            AkeneoFamilyCode = configuration.AkeneoFamilyCode,
            Enabled = configuration.Enabled,
            PreserveExistingNopVariantStructure = configuration.PreserveExistingNopVariantStructure,
            Mode = configuration.VariantRelationshipMode,
            AssociatedProductAttributeId = configuration.AssociatedProductAttributeId,
            AssociatedValueNameTemplate = string.IsNullOrWhiteSpace(configuration.AssociatedValueNameTemplate)
                ? "{axes}"
                : configuration.AssociatedValueNameTemplate,
            HideChildProductsWhenRepresentedByParent = configuration.HideChildProductsWhenRepresentedByParent,
            AxisMappings = axisMappings
                .OrderBy(x => x.DisplayOrder)
                .ThenBy(x => x.Id)
                .ToList()
        };
    }
}