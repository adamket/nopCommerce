using Apt.Nop.Plugin.Misc.TopicsPlus.Domain;
using Nop.Core;
using Nop.Core.Domain.Topics;

namespace Apt.Nop.Plugin.Misc.TopicsPlus.Services;
public interface ITopicRevisionService
{
    Task CreateRevisionAsync(Topic topic, int customerId);
    Task<TopicRevision> GetTopicRevisionByIdAsync(int topicRevisionId);

    Task<IPagedList<TopicRevision>> SearchTopicRevisionsAsync(
        int? topicId = null,
        string title = null,
        DateTime? createdFrom = null,
        DateTime? createdTo = null,
        int pageIndex = 0,
        int pageSize = int.MaxValue);

    Task DeleteTopicRevisionAsync(TopicRevision revision);
    Task DeleteTopicRevisionsAsync(IList<TopicRevision> revisions);
}
