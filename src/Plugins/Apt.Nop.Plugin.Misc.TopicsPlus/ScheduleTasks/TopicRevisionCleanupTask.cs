using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Apt.Nop.Plugin.Misc.TopicsPlus.Services;
using Nop.Services.ScheduleTasks;

namespace Apt.Nop.Plugin.Misc.TopicsPlus.ScheduleTasks;
public class TopicRevisionCleanupTask(ITopicRevisionService topicRevisionService, TopicsPlusSettings settings) : IScheduleTask
{

    public async Task ExecuteAsync()
    {
        //var cutoffDate = DateTime.UtcNow.AddDays(settings.RevisionRetentionCount > 0  ? -settings.RevisionRetentionDays : -7);
        //var topicRevisions = await topicRevisionService.SearchTopicRevisionsAsync(createdTo: cutoffDate);
        //await topicRevisionService.DeleteTopicRevisionsAsync(topicRevisions);
    }
}
