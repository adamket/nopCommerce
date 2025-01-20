using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Apt.Nop.Plugin.Misc.NopCms.Domain;
using Apt.Nop.Plugin.Misc.NopCms.Services;
using Nop.Core;
using Nop.Core.Domain.Topics;
using Nop.Core.Events;
using Nop.Services.Events;

namespace Apt.Nop.Plugin.Misc.NopCms.EventConsumer;
public class TopicEventConsumer(TopicEntryService _topicEntryService, IWorkContext _workContext) : IConsumer<EntityUpdatedEvent<Topic>>,
    IConsumer<EntityDeletedEvent<Topic>>, IConsumer<EntityInsertedEvent<Topic>>
{


    public async Task HandleEventAsync(EntityUpdatedEvent<Topic> eventMessage)
    {
        //if (await _topicEntryService.ShouldAddVersionAsync(eventMessage.Entity))
        //{
        //    await _topicEntryService.InsertTopicEntryAsync(eventMessage.Entity, (await _workContext.GetCurrentCustomerAsync())?.Id);
        //}
    }

    public async Task HandleEventAsync(EntityDeletedEvent<Topic> eventMessage)
    {
        
    }

    public async Task HandleEventAsync(EntityInsertedEvent<Topic> eventMessage)
    {
     //   await _topicEntryService.InsertTopicEntryAsync(eventMessage.Entity);
    }
}
