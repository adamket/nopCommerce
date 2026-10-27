using Nop.Services.Configuration;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

/// <summary>
/// Records when a mapping that shapes product output was last saved, so the
/// sync profile page can recommend a resync for products Akeneo has not changed.
/// </summary>
public interface IAkeneoMappingChangeTracker
{
    Task MarkChangedAsync();

    Task<DateTime?> GetChangedOnUtcAsync();
}

public class AkeneoMappingChangeTracker(ISettingService settingService) : IAkeneoMappingChangeTracker
{
    // One save writes several rows (a mapping and its fallbacks); one setting write covers them.
    private static readonly TimeSpan Coalesce = TimeSpan.FromSeconds(10);

    private string Key => settingService.GetSettingKey(
        new AkeneoConnectionSettings(),
        settings => settings.MappingsChangedOnUtc);

    public async Task MarkChangedAsync()
    {
        var now = DateTime.UtcNow;
        var last = await GetChangedOnUtcAsync();
        if (last.HasValue && now - last.Value < Coalesce)
            return;

        await settingService.SetSettingAsync<DateTime?>(Key, now);
    }

    public Task<DateTime?> GetChangedOnUtcAsync() =>
        settingService.GetSettingByKeyAsync<DateTime?>(Key);
}
