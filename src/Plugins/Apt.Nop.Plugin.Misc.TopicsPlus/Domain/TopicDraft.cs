using Nop.Core;

namespace Apt.Nop.Plugin.Misc.TopicsPlus.Domain;
public class TopicDraft : BaseEntity
{
    public int TopicId { get; set; }
    public string Title { get; set; }
    public string Body { get; set; }

    public int CreatedByCustomerId { get; set; }
    public int UpdatedByCustomerId { get; set; }
    public int PublishScheduleByCustomerId { get; set; }
    public DateTime CreatedOnUtc { get; set; }
    public DateTime? UpdatedOnUtc { get; set; }
    public DateTime? PublishScheduledOnUtc { get; set; }
}
