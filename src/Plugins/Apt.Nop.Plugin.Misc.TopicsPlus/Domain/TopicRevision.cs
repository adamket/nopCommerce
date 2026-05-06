using Nop.Core;

namespace Apt.Nop.Plugin.Misc.TopicsPlus.Domain;
public class TopicRevision : BaseEntity
{
    public int TopicId { get; set; }

    public string SystemName { get; set; }
    public string Title { get; set; }
    public string Body { get; set; }

    public bool Published { get; set; }
    public int DisplayOrder { get; set; }

    public int CustomerId { get; set; }
    public DateTime CreatedOnUtc { get; set; }

    public string ChangeNote { get; set; }
    public bool IsRevert { get; set; }
}