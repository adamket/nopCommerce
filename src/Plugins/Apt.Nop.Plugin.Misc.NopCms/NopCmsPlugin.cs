using Apt.Nop.Plugin.Misc.NopCms.Services;
using Nop.Core;
using Nop.Services.Cms;
using Nop.Services.Common;
using Nop.Services.Plugins;
using Nop.Services.Topics;
using Nop.Web.Framework.Infrastructure;

namespace Apt.Nop.Plugin.Misc.NopCms;

public class NopCmsPlugin(ITopicService _topicService, TopicEntryService _topicEntryService, IWebHelper _webHelper) : BasePlugin, IMiscPlugin
{

    public override string GetConfigurationPageUrl()
    {
        return _webHelper.GetStoreLocation() + "Admin/NopCms/Configure";
    }

    public override async Task InstallAsync()
    {
        await base.InstallAsync();

        var allTopics = await _topicService.GetAllTopicsAsync(0, showHidden: true);
        foreach (var topic in allTopics)
        {
            if (await _topicEntryService.ShouldAddVersionAsync(topic))
            {
                await _topicEntryService.InsertTopicEntryAsync(topic);
            }
        }
    }

    public override async Task UninstallAsync()
    {
        await base.UninstallAsync();
    }

    public override async Task UpdateAsync(string currentVersion, string targetVersion)
    {
        await base.UpdateAsync(currentVersion, targetVersion);
    }

    public override async Task PreparePluginToUninstallAsync()
    {
        await base.PreparePluginToUninstallAsync();
    }

//    public bool HideInWidgetList => false;

//    public  Task<IList<string>> GetWidgetZonesAsync()
//    {
//        return Task.FromResult<IList<string>>(new List<string>
//        {
//            AdminWidgetZones.TopicDetailsBlock,
//        });
//    }

//    public Type GetWidgetViewComponent(string widgetZone)
//    {
//        switch (widgetZone)
//        {
//            case "admin_topic_details_block": return typeof(NopCmsAdminTopicBlockViewComponent);
//            default: return null;
//        }

//    }
}
