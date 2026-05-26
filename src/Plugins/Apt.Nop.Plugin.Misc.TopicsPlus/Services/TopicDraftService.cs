using Apt.Nop.Plugin.Misc.TopicsPlus.Domain;
using Nop.Data;

namespace Apt.Nop.Plugin.Misc.TopicsPlus.Services;
public class TopicDraftService(IRepository<TopicDraft> topicDraftRepository) : ITopicDraftService
{
    public async Task InsertTopicDraft(TopicDraft topicDraft)
    {
        await topicDraftRepository.InsertAsync(topicDraft);
    }

    public async Task UpdateTopicDraft(TopicDraft topicDraft)
    {
        await topicDraftRepository.UpdateAsync(topicDraft);
    }

    public async Task DeleteTopicDraft(TopicDraft topicDraft)
    {
        await topicDraftRepository.DeleteAsync(topicDraft);
    }

    public async Task<TopicDraft> GetTopicDraftById(int id)
    {
        return await topicDraftRepository.GetByIdAsync(id);
    }
}
