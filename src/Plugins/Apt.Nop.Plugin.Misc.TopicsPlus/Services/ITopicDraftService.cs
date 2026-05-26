using Apt.Nop.Plugin.Misc.TopicsPlus.Domain;

namespace Apt.Nop.Plugin.Misc.TopicsPlus.Services;
public interface ITopicDraftService
{
    public Task InsertTopicDraft(TopicDraft topicDraft);
    public Task UpdateTopicDraft(TopicDraft topicDraft);
    public Task DeleteTopicDraft(TopicDraft topicDraft);

    public Task<TopicDraft> GetTopicDraftById(int id);
}
