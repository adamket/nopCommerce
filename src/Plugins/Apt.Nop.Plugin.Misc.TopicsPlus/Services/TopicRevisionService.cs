using Apt.Nop.Plugin.Misc.TopicsPlus.Domain;
using Nop.Core;
using Nop.Core.Domain.Topics;
using Nop.Data;

namespace Apt.Nop.Plugin.Misc.TopicsPlus.Services;

public class TopicRevisionService(IRepository<TopicRevision> topicRevisionRepository) : ITopicRevisionService
{
    public async Task CreateRevisionAsync(Topic topic, int customerId)
    {
        var latest = await GetLatestRevisionAsync(topic.Id);

        if (latest != null &&
            latest.Title == topic.Title &&
            latest.Body == topic.Body &&
            latest.SystemName == topic.SystemName &&
            latest.Published == topic.Published)
        {
            return;
        }

        var revision = new TopicRevision
        {
            TopicId = topic.Id,
            SystemName = topic.SystemName,
            Title = topic.Title,
            Body = topic.Body,
            Published = topic.Published,
            DisplayOrder = topic.DisplayOrder,
            CustomerId = customerId,
            CreatedOnUtc = DateTime.UtcNow
        };

        await topicRevisionRepository.InsertAsync(revision);
    }



    public async Task<IPagedList<TopicRevision>> SearchTopicRevisionsAsync(
        int? topicId = null,
        string systemName = null,
        string title = null,
        bool? published = null,
        DateTime? createdFrom = null,
        DateTime? createdTo = null,
        int pageIndex = 0,
        int pageSize = int.MaxValue)
    {
        return await topicRevisionRepository.GetAllPagedAsync(query =>
        {
            if (topicId > 0)
                query = query.Where(x => x.TopicId == topicId);

            if (!string.IsNullOrWhiteSpace(systemName))
                query = query.Where(x => x.SystemName.Contains(systemName));

            if (!string.IsNullOrWhiteSpace(title))
                query = query.Where(x => x.Title.Contains(title));

            if (published.HasValue)
                query = query.Where(x => x.Published == published.Value);
            
            if (createdFrom.HasValue)
                query = query.Where(x => x.CreatedOnUtc >= createdFrom.Value);

            if (createdTo.HasValue)
                query = query.Where(x => x.CreatedOnUtc <= createdTo.Value);

            query = query.OrderByDescending(x => x.CreatedOnUtc);

            return query;
        }, pageIndex, pageSize);
    }


    public async Task<TopicRevision> GetTopicRevisionByIdAsync(int topicRevisionId)
    {
        //use the built-in getbyid for caching?
        return await topicRevisionRepository.Table.FirstOrDefaultAsync(q => q.Id == topicRevisionId);
    }

    public async Task DeleteTopicRevisionAsync(TopicRevision revision)
    {
        await topicRevisionRepository.DeleteAsync(revision);
    }

    public async Task DeleteTopicRevisionsAsync(IList<TopicRevision> revisions)
    {
        await topicRevisionRepository.DeleteAsync(revisions);
    }

    private async Task<TopicRevision> GetLatestRevisionAsync(int topicId)
    {
        var revision = await topicRevisionRepository.Table
            .Where(r => r.TopicId == topicId)
            .OrderByDescending(r => r.CreatedOnUtc)
            .FirstOrDefaultAsync();

        return revision;
    }
}