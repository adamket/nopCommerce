using Apt.Nop.Plugin.Misc.NopCms.Domain;
using Nop.Core;
using Nop.Core.Caching;
using Nop.Core.Domain.Topics;
using Nop.Data;

namespace Apt.Nop.Plugin.Misc.NopCms.Services;
public class TopicEntryService(IRepository<TopicEntry> _topicEntryRepository, IStaticCacheManager _cacheManager)
{

    public async Task InsertTopicEntryAsync(Topic topic, int? customerId = null)
    {
        var version = GetLatestVersionAsync(topic);
        var topicEntry = new TopicEntry
        {
            Body = topic.Body,
            Title = topic.Title,
            TopicId = topic.Id,
            CustomerId = customerId,
            Version = (await GetLatestVersionAsync(topic)) + 1
        };

        await InsertTopicEntryAsync(topicEntry);
    }

    public async Task InsertTopicEntryAsync(TopicEntry topicEntry)
    {
        topicEntry.CreatedOnUtc = DateTime.UtcNow;
        
        await _topicEntryRepository.InsertAsync(topicEntry);
        await _cacheManager.RemoveAsync(new CacheKey($"topic-entries-by-topic-{topicEntry.TopicId}"));
    }

    public async Task UpdateTopicEntryAsync(TopicEntry topicEntry)
    {
        await _topicEntryRepository.UpdateAsync(topicEntry);
        await _cacheManager.RemoveAsync(new CacheKey($"topic-entries-by-topic-{topicEntry.TopicId}"));
    }

    public async Task ArchiveTopicEntryAsync(TopicEntry topicEntry)
    {
        topicEntry.TopicEntryStatusId = (int)TopicEntryStatus.Archived;
        await _topicEntryRepository.UpdateAsync(topicEntry);
        await _cacheManager.RemoveAsync(new CacheKey($"topic-entries-by-topic-{topicEntry.TopicId}"));
    }

    public async Task DeleteTopicEntryAsync(TopicEntry topicEntry)
    {
        await _topicEntryRepository.DeleteAsync(topicEntry);
        await _cacheManager.RemoveAsync(new CacheKey($"topic-entries-by-topic-{topicEntry.TopicId}"));
    }

    public async Task<IPagedList<TopicEntry>> GetTopicEntriesByTopicIdAsync(int topicId, bool showHidden = false, int pageIndex = 0, int pageSize = int.MaxValue)
    {
        var topicEntryCacheKey = new CacheKey($"topic-entries-by-topic-{topicId}")
        {
            CacheTime = 60 * 60 * 12 //12 hrs?
        };
        var topicEntries = await _cacheManager.GetAsync(topicEntryCacheKey, () =>
        {
            var ct = _topicEntryRepository.Table.Where(q => q.TopicId == topicId 
                                                            && q.TopicEntryStatusId != (int)TopicEntryStatus.Archived).OrderByDescending(q=>q.CreatedOnUtc);
            return ct.ToList();
        });

        return new PagedList<TopicEntry>(topicEntries, pageIndex, pageSize);
    }



    private async Task<int> GetLatestVersionAsync(Topic topic)
    {
        var entries = await GetTopicEntriesByTopicIdAsync(topic.Id, true);
        if (!entries.Any())
        {
            return 0;
        }

        var maxVersion = entries.Max(q => q.Version);
        return maxVersion;
    }

    public async Task<bool> ShouldAddVersionAsync(Topic topic)
    {
        var mostRecentTopicEntry = (await GetTopicEntriesByTopicIdAsync(topic.Id)).FirstOrDefault();
        if (mostRecentTopicEntry == null)
        {
            return true;
        }

        var shouldAdd = topic.Body != mostRecentTopicEntry.Body || topic.Title != mostRecentTopicEntry.Title;
        return shouldAdd;
    }
}
