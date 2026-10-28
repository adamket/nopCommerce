using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Nop.Data;
using Nop.Services.Configuration;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public interface IAkeneoCategoryContentMappingService
{
    Task<IList<AkeneoCategoryContentMapping>> GetAllMappingsAsync();

    Task<AkeneoCategoryContentMapping> GetMappingByIdAsync(int id);

    Task InsertMappingAsync(AkeneoCategoryContentMapping mapping);

    Task UpdateMappingAsync(AkeneoCategoryContentMapping mapping);

    Task DeleteMappingAsync(AkeneoCategoryContentMapping mapping);

    Task<IList<AkeneoCategoryContentState>> GetAllStatesAsync();

    Task<AkeneoCategoryContentState> GetStateAsync(string akeneoCategoryCode);

    Task SaveStateAsync(AkeneoCategoryContentState state);

    /// <summary>The Akeneo locales category content is translated into, in saved order.</summary>
    Task<IList<string>> GetTranslationLocalesAsync();

    Task SaveTranslationLocalesAsync(IEnumerable<string> locales);
}

/// <summary>
/// Category content mappings and per-category sync state. Both tables hold a
/// handful of rows, so they are read straight from the database.
/// </summary>
public class AkeneoCategoryContentMappingService(
    IRepository<AkeneoCategoryContentMapping> mappingRepository,
    IRepository<AkeneoCategoryContentState> stateRepository,
    ISettingService settingService)
    : IAkeneoCategoryContentMappingService
{
    public async Task<IList<AkeneoCategoryContentMapping>> GetAllMappingsAsync() =>
        await mappingRepository.GetAllAsync(query => query
            .OrderBy(mapping => mapping.TargetId)
            .ThenBy(mapping => mapping.LanguageId)
            .ThenBy(mapping => mapping.Id));

    public async Task<AkeneoCategoryContentMapping> GetMappingByIdAsync(int id) =>
        id > 0 ? await mappingRepository.GetByIdAsync(id) : null;

    public async Task InsertMappingAsync(AkeneoCategoryContentMapping mapping)
    {
        mapping.CreatedOnUtc = mapping.UpdatedOnUtc = DateTime.UtcNow;
        await mappingRepository.InsertAsync(mapping, false);
    }

    public async Task UpdateMappingAsync(AkeneoCategoryContentMapping mapping)
    {
        mapping.UpdatedOnUtc = DateTime.UtcNow;
        await mappingRepository.UpdateAsync(mapping, false);
    }

    public async Task DeleteMappingAsync(AkeneoCategoryContentMapping mapping) =>
        await mappingRepository.DeleteAsync(mapping, false);

    public async Task<IList<AkeneoCategoryContentState>> GetAllStatesAsync() =>
        await stateRepository.GetAllAsync(query => query);

    public async Task<AkeneoCategoryContentState> GetStateAsync(string akeneoCategoryCode)
    {
        if (string.IsNullOrWhiteSpace(akeneoCategoryCode))
            return null;

        var code = akeneoCategoryCode.Trim();
        return await stateRepository.Table
            .Where(state => state.AkeneoCategoryCode == code)
            .OrderBy(state => state.Id)
            .FirstOrDefaultAsync();
    }

    public async Task SaveStateAsync(AkeneoCategoryContentState state)
    {
        state.UpdatedOnUtc = DateTime.UtcNow;

        if (state.Id > 0)
            await stateRepository.UpdateAsync(state, false);
        else
            await stateRepository.InsertAsync(state, false);
    }

    private string TranslationLocalesKey => settingService.GetSettingKey(
        new AkeneoConnectionSettings(),
        settings => settings.CategoryContentTranslationLocales);

    public async Task<IList<string>> GetTranslationLocalesAsync() =>
        AkeneoCategoryContentPlanner.ParseLocales(await settingService.GetSettingByKeyAsync<string>(TranslationLocalesKey));

    public async Task SaveTranslationLocalesAsync(IEnumerable<string> locales) =>
        await settingService.SetSettingAsync(TranslationLocalesKey, string.Join(",", locales ?? []));
}
