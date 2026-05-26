using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Apt.Nop.Plugin.Misc.TopicsPlus.Domain;
using Nop.Core;
using Nop.Core.Domain.Topics;
using Nop.Services.Topics;

namespace Apt.Nop.Plugin.Misc.TopicsPlus.Services;
public interface ICustomTopicService : ITopicService
{
    Task UpdateTopicAsync(Topic topic, bool publishEvent = true);
    Task<IList<TopicData>> GetAllTopicDataAsync(string widgetZone = null, int pageIndex = 0, int pageSize = int.MaxValue);
    Task UpdateTopicDataAsync(TopicData topicData);
    Task InsertTopicDataAsync(TopicData topicData);
    Task<TopicData> GetTopicDataByTopicIdAsync(int topicId);
}
