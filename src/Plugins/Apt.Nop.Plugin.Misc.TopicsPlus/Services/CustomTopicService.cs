using Nop.Core;
using Nop.Core.Caching;
using Nop.Core.Domain.Topics;
using Nop.Data;
using Nop.Services.Customers;
using Nop.Services.Logging;
using Nop.Services.Security;
using Nop.Services.Stores;
using Nop.Services.Topics;

namespace Apt.Nop.Plugin.Misc.TopicsPlus.Services;
public class CustomTopicService(
    ILogger logger,
    IAclService aclService,
    ICustomerService customerService,
    IRepository<Topic> topicRepository,
    IStaticCacheManager staticCacheManager,
    IStoreMappingService storeMappingService,
    IWorkContext workContext,
    ITopicRevisionService topicRevisionService)
    : TopicService(aclService, customerService, topicRepository, staticCacheManager, storeMappingService, workContext),
        ITopicService
{
    public override async Task UpdateTopicAsync(Topic topic)
    {
        var existingTopic = await _topicRepository.Table.FirstOrDefaultAsync(q => q.Id == topic.Id);
        await base.UpdateTopicAsync(topic);
        try
        {
            await topicRevisionService.CreateRevisionAsync(topic,
                (await _workContext.GetCurrentCustomerAsync())?.Id ?? 0);
        }
        catch (Exception e)
        {
            await logger.ErrorAsync("Topics+: Error creating topic revision.", e);
        }

    }
}
