using Apt.Nop.Plugin.Misc.TopicsPlus.Domain;
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
    ITopicRevisionService topicRevisionService,
    IRepository<TopicData> topicDataRepository)
    : TopicService(aclService, customerService, topicRepository, staticCacheManager, storeMappingService, workContext),
        ICustomTopicService
{
    public async Task UpdateTopicAsync(Topic topic, bool publishEvent = true)
    {
       await _topicRepository.UpdateAsync(topic, publishEvent);
    }

    public async Task<TopicData> GetTopicDataByTopicIdAsync(int topicId)
    {
        var cacheKey = staticCacheManager.PrepareKeyForDefaultCache(TopicsPlusConstants.CacheKeys.TopicDataByTopicIdCacheKey, topicId);
        var result = await staticCacheManager.GetAsync(cacheKey,() =>
        {
            return topicDataRepository.Table.FirstOrDefault(q => q.TopicId == topicId);
        });

        return result;
    }

    public async Task<IList<TopicData>> GetAllTopicDataAsync(string widgetZone = null, int pageIndex = 0, int pageSize = int.MaxValue)
    {
        var cacheKey = staticCacheManager.PrepareKeyForDefaultCache(TopicsPlusConstants.CacheKeys.AllTopicDataCacheKey);
        var result = await staticCacheManager.GetAsync(cacheKey, () => topicDataRepository.Table.ToList());


        if (widgetZone != null)
        {
            result = result.Where(q =>
                    q.WidgetZones.Split(",", StringSplitOptions.RemoveEmptyEntries).Any(wz => wz.Equals(widgetZone)))
                .ToList();
        }
        return result;
    }

    public async Task UpdateTopicDataAsync(TopicData topicData)
    {
        if (topicData == null)
            throw new ArgumentNullException(nameof(topicData));

        await topicDataRepository.UpdateAsync(topicData);
    }

    public async Task InsertTopicDataAsync(TopicData topicData)
    {
        if (topicData == null)
            throw new ArgumentNullException(nameof(topicData));
        await topicDataRepository.InsertAsync(topicData);
    }
}
