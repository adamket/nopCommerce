using Apt.Nop.Plugin.Misc.TopicsPlus.Domain;
using Apt.Nop.Plugin.Misc.TopicsPlus.Services;
using Nop.Core;
using Nop.Core.Caching;
using Nop.Core.Domain.Topics;
using Nop.Core.Events;
using Nop.Services.Events;

namespace Apt.Nop.Plugin.Misc.TopicsPlus.EventConsumers;

public class TopicEventConsumer(
    IStaticCacheManager staticCacheManager,
    ITopicRevisionService topicRevisionService,
    IWorkContext workContext)
    : IConsumer<EntityUpdatedEvent<Topic>>, IConsumer<EntityUpdatedEvent<TopicData>>, IConsumer<EntityDeletedEvent<TopicData>>
{
    public async Task HandleEventAsync(EntityUpdatedEvent<Topic> eventMessage)
    {
        var topic = eventMessage.Entity;
        await topicRevisionService.CreateRevisionAsync(topic, (await workContext.GetCurrentCustomerAsync())?.Id ?? 0);
    }

    public async Task HandleEventAsync(EntityUpdatedEvent<TopicData> eventMessage)
    {
        await staticCacheManager.RemoveAsync(TopicsPlusConstants.CacheKeys.TopicDataByTopicIdCacheKey, eventMessage.Entity.TopicId);
        await staticCacheManager.RemoveAsync(TopicsPlusConstants.CacheKeys.AllTopicDataCacheKey);
    }

    public async Task HandleEventAsync(EntityDeletedEvent<TopicData> eventMessage)
    {
        await staticCacheManager.RemoveAsync(TopicsPlusConstants.CacheKeys.TopicDataByTopicIdCacheKey, eventMessage.Entity.TopicId);
        await staticCacheManager.RemoveAsync(TopicsPlusConstants.CacheKeys.AllTopicDataCacheKey);
    }
}