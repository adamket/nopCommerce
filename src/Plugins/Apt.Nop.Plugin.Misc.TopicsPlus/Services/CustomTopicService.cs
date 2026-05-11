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
        ICustomTopicService
{
    public async Task UpdateTopicAsync(Topic topic, bool publishEvent = true)
    {
       await _topicRepository.UpdateAsync(topic, publishEvent);
    }
}
