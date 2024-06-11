using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Plugins;

namespace Aperture.Nop.Plugin.Misc.FullPageCache;

public class FullPageCachePlugin : BasePlugin, IMiscPlugin
{
    private readonly ISettingService _settingService;
    private readonly ILocalizationService _localizationService;
    public FullPageCachePlugin(ISettingService settingService, ILocalizationService localizationService)
    {
        _settingService = settingService;
        _localizationService = localizationService;
    }


    /// <summary>
    /// Install the plugin
    /// </summary>
    /// <returns>A task that represents the asynchronous operation</returns>
    public override async Task InstallAsync()
    {
        //settings
        await _settingService.SaveSettingAsync(new FullPageCacheSettings());



        await base.InstallAsync();
    }

    /// <summary>
    /// Uninstall the plugin
    /// </summary>
    /// <returns>A task that represents the asynchronous operation</returns>
    public override async Task UninstallAsync()
    {
        //settings
        await _settingService.DeleteSettingAsync<FullPageCacheSettings>();

        //locales
        await _localizationService.DeleteLocaleResourcesAsync("Aperture.Nop.Plugin.FullPageCache");

        await base.UninstallAsync();
    }

}
