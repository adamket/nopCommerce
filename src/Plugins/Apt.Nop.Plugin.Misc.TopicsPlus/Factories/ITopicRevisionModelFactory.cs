using Apt.Nop.Plugin.Misc.TopicsPlus.Domain;
using Apt.Nop.Plugin.Misc.TopicsPlus.Models;
using Nop.Core.Domain.Customers;

namespace Apt.Nop.Plugin.Misc.TopicsPlus.Factories;
public interface ITopicRevisionModelFactory
{
    Task<TopicRevisionSearchModel> PrepareTopicRevisionSearchModelAsync(TopicRevisionSearchModel searchModel);

    Task<TopicRevisionListModel> PrepareTopicRevisionListModelAsync(TopicRevisionSearchModel searchModel);

    Task<TopicRevisionModel> PrepareTopicRevisionModelAsync(
        TopicRevisionModel model,
        TopicRevision revision,
        Customer customer,
        bool excludeProperties = false);
}
