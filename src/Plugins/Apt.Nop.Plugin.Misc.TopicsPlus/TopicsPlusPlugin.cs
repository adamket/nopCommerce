using Apt.Nop.Plugin.Misc.TopicsPlus.Components;
using Apt.Nop.Plugin.Misc.TopicsPlus.Domain;
using Apt.Nop.Plugin.Misc.TopicsPlus.ScheduleTasks;
using Apt.Nop.Plugin.Misc.TopicsPlus.Services;
using Nop.Core;
using Nop.Core.Domain.ScheduleTasks;
using Nop.Services.Cms;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Plugins;
using Nop.Services.ScheduleTasks;
using Nop.Services.Topics;
using Nop.Web.Framework.Infrastructure;

namespace Apt.Nop.Plugin.Misc.TopicsPlus;

/// <summary>
/// CheckMoneyOrder payment processor
/// </summary>
public class TopicsPlusPlugin(
    ILocalizationService localizationService,
    ISettingService settingService,
    IWebHelper webHelper,
    ITopicService topicService,
    ITopicRevisionService topicRevisionService,
    IScheduleTaskService scheduleTaskService)
    : BasePlugin, IMiscPlugin, IWidgetPlugin
{


    #region Methods

    /// <summary>
    /// Install the plugin
    /// </summary>
    /// <returns>A task that represents the asynchronous operation</returns>
    public override async Task InstallAsync()
    {

        var topics = await topicService.GetAllTopicsAsync(0);

        foreach (var topic in topics)
        {
            await topicRevisionService.CreateRevisionAsync(topic, 0);
        }

        var taskType = typeof(TopicRevisionCleanupTask).FullName
                       ?? throw new InvalidOperationException("Unable to resolve schedule task type.");

        var revisionCleanupTask = await scheduleTaskService.GetTaskByTypeAsync(taskType);
        if (revisionCleanupTask == null)
        {
            var cleanupTask = new ScheduleTask
            {
                Enabled = true,
                Seconds = 60 * 60 * 24, // Run once a day
                Type = taskType,
                Name = "Topics+ - Topic Revision Cleanup Task",
                
            };
            await scheduleTaskService.InsertTaskAsync(cleanupTask);
        }


        await base.InstallAsync();
    }

    /// <summary>
    /// Uninstall the plugin
    /// </summary>
    /// <returns>A task that represents the asynchronous operation</returns>
    public override async Task UninstallAsync()
    {
        var pageIndex = 0;
        const int pageSize = 25;

        IList<TopicRevision> topicRevisions;
        do
        {
            topicRevisions =
                await topicRevisionService.SearchTopicRevisionsAsync(pageIndex: pageIndex, pageSize: pageSize);

            await topicRevisionService.DeleteTopicRevisionsAsync(topicRevisions);
        } while (topicRevisions.Count == pageSize);

        var taskType = typeof(TopicRevisionCleanupTask).FullName
                       ?? throw new InvalidOperationException("Unable to resolve schedule task type.");

        var revisionCleanupTask = await scheduleTaskService.GetTaskByTypeAsync(taskType);
        if (revisionCleanupTask != null)
        {
            await scheduleTaskService.DeleteTaskAsync(revisionCleanupTask);
        }

        await base.UninstallAsync();
    }
   
    #endregion

    public bool HideInWidgetList => false;
    public Task<IList<string>> GetWidgetZonesAsync()
    {
        return Task.FromResult<IList<string>>(new List<string> { AdminWidgetZones.TopicDetailsBlock });
    }

    public Type GetWidgetViewComponent(string widgetZone)
    {
        return typeof(TopicsPlusViewComponent);
    }

    public override string GetConfigurationPageUrl()
    {
        return $"{webHelper.GetStoreLocation()}admin/apt/topics-plus/configure";
    }
}