using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Nop.Data;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public class AkeneoFamilyMappingService(
    IRepository<AkeneoFamilyMapping> configurationRepository,
    IRepository<AkeneoFamilyVariantAxisMapping> axisMappingRepository, IRepository<AkeneoFamilySubModelRule> subModelRuleRepository)
    : IAkeneoFamilyMappingService
{
    public async Task<AkeneoFamilyMapping> GetByIdAsync(int id)
    {
        return await configurationRepository.GetByIdAsync(id);
    }

    public async Task<AkeneoFamilyMapping> GetByFamilyCodeAsync(string akeneoFamilyCode)
    {
        return await GetByFamilyAndVariantCodeAsync(akeneoFamilyCode, null);
    }

    public async Task<AkeneoFamilyMapping> GetByFamilyAndVariantCodeAsync(
        string akeneoFamilyCode,
        string akeneoFamilyVariantCode)
    {
        if (string.IsNullOrWhiteSpace(akeneoFamilyCode))
            return null;

        akeneoFamilyCode = akeneoFamilyCode.Trim();
        akeneoFamilyVariantCode = NormalizeVariantCode(akeneoFamilyVariantCode);

        return await configurationRepository.Table
            .FirstOrDefaultAsync(x =>
                x.AkeneoFamilyCode == akeneoFamilyCode &&
                (x.AkeneoFamilyVariantCode ?? string.Empty) ==
                    akeneoFamilyVariantCode);
    }

    public async Task<AkeneoFamilyMapping> GetEffectiveMappingAsync(
        string akeneoFamilyCode,
        string akeneoFamilyVariantCode)
    {
        if (string.IsNullOrWhiteSpace(akeneoFamilyCode))
            return null;

        var normalizedVariantCode = NormalizeVariantCode(
            akeneoFamilyVariantCode);

        if (!string.IsNullOrWhiteSpace(normalizedVariantCode))
        {
            var exact = await GetByFamilyAndVariantCodeAsync(
                akeneoFamilyCode,
                normalizedVariantCode);

            if (exact is { Enabled: true })
                return exact;
        }

        var familyDefault = await GetByFamilyCodeAsync(akeneoFamilyCode);
        return familyDefault is { Enabled: true }
            ? familyDefault
            : null;
    }

    public async Task<IList<AkeneoFamilyMapping>> GetAllAsync()
    {
        return await configurationRepository.Table
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.AkeneoFamilyCode)
            .ThenBy(x => x.AkeneoFamilyVariantCode)
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

    public async Task InsertAsync(AkeneoFamilyMapping configuration)
    {
        configuration.CreatedOnUtc = DateTime.UtcNow;
        configuration.UpdatedOnUtc = DateTime.UtcNow;

        await configurationRepository.InsertAsync(configuration);
    }

    public async Task UpdateAsync(AkeneoFamilyMapping configuration)
    {
        configuration.UpdatedOnUtc = DateTime.UtcNow;

        await configurationRepository.UpdateAsync(configuration);
    }

    public async Task DeleteAsync(AkeneoFamilyMapping configuration)
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

    public async Task<AkeneoVariantRelationshipOptions> BuildOptionsForFamilyAsync(
        string akeneoFamilyCode,
        string akeneoFamilyVariantCode = null)
    {
        var configuration = await GetEffectiveMappingAsync(
            akeneoFamilyCode,
            akeneoFamilyVariantCode);

        if (configuration == null || !configuration.Enabled)
            return null;

        var axisMappings = await GetAxisMappingsAsync(configuration.Id);

        return new AkeneoVariantRelationshipOptions
        {
            FamilyVariantImportConfigurationId = configuration.Id,
            AkeneoFamilyCode = configuration.AkeneoFamilyCode,
            AkeneoFamilyVariantCode = NormalizeVariantCode(
                configuration.AkeneoFamilyVariantCode),
            Enabled = configuration.Enabled,
            PreserveExistingNopVariantStructure = configuration.PreserveExistingNopVariantStructure,
            Mode = configuration.VariantRelationshipMode,
            ProductModelHierarchyMode = configuration.ProductModelHierarchyMode,
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

    private static string NormalizeVariantCode(string value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    public async Task<IList<AkeneoFamilySubModelRule>> GetSubModelRulesAsync(int familyMappingId)
    {
        return await subModelRuleRepository.Table
            .Where(x => x.FamilyMappingId == familyMappingId)
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Id)
            .ToListAsync();
    }

    public async Task InsertSubModelRuleAsync(AkeneoFamilySubModelRule rule)
    {
        rule.CreatedOnUtc = DateTime.UtcNow;
        rule.UpdatedOnUtc = DateTime.UtcNow;
        await subModelRuleRepository.InsertAsync(rule);
    }

    public async Task UpdateSubModelRuleAsync(AkeneoFamilySubModelRule rule)
    {
        rule.UpdatedOnUtc = DateTime.UtcNow;
        await subModelRuleRepository.UpdateAsync(rule);
    }

    public async Task DeleteSubModelRuleAsync(AkeneoFamilySubModelRule rule)
    {
        await subModelRuleRepository.DeleteAsync(rule);
    }
}
