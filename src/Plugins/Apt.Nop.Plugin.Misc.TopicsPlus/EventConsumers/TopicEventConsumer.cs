using Apt.Nop.Plugin.Misc.TopicsPlus.Services;
using Nop.Core;
using Nop.Core.Domain.Topics;
using Nop.Core.Events;
using Nop.Services.Events;

namespace Apt.Nop.Plugin.Misc.TopicsPlus.EventConsumers;

public class TopicEventConsumer(
    ITopicRevisionService topicRevisionService,
    IWorkContext workContext)
    : IConsumer<EntityUpdatedEvent<Topic>>
{
    public async Task HandleEventAsync(EntityUpdatedEvent<Topic> eventMessage)
    {
        var topic = eventMessage.Entity;
        await topicRevisionService.CreateRevisionAsync(topic, (await workContext.GetCurrentCustomerAsync())?.Id ?? 0);
    }
}