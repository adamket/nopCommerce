using Nop.Services.Common;
using Nop.Services.Plugins;

namespace Apt.Nop.Plugin.Misc.NopCms;

public class NopCmsPlugin : IMiscPlugin
{
    public string GetConfigurationPageUrl()
    {
        return $"";
    }

    public PluginDescriptor PluginDescriptor { get; set; }
    public async Task InstallAsync()
    {
    }

    public async Task UninstallAsync()
    {
        
    }

    public async Task UpdateAsync(string currentVersion, string targetVersion)
    {
        
    }

    public async Task PreparePluginToUninstallAsync()
    {

    }
}
