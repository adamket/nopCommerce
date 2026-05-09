using Apt.Nop.Plugin.Misc.TopicsPlus.Domain;
using Apt.Nop.Plugin.Misc.TopicsPlus.Models;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Topics;

namespace Apt.Nop.Plugin.Misc.TopicsPlus.Factories;
public interface ITopicRevisionModelFactory
{
    Task<TopicRevisionSearchModel> PrepareTopicRevisionSearchModelAsync(TopicRevisionSearchModel searchModel);

    Task<TopicRevisionListModel> PrepareTopicRevisionListModelAsync(TopicRevisionSearchModel searchModel, Topic topic = null);

    Task<TopicRevisionModel> PrepareTopicRevisionModelAsync(
        TopicRevisionModel model,
        TopicRevision revision,
        Topic topic,
        Customer customer,
        bool excludeProperties = false);
}
