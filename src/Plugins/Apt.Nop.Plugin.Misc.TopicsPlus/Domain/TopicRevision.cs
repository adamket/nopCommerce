using LinqToDB.Mapping;
using Nop.Core;

namespace Apt.Nop.Plugin.Misc.TopicsPlus.Domain;

[Table("Apt_TopicRevision")]
public class TopicRevision : BaseEntity
{
    public int TopicId { get; set; }
    public string Title { get; set; }
    public string Body { get; set; }
    public int CustomerId { get; set; }
    public DateTime CreatedOnUtc { get; set; }
    public int Version { get; set; }
    public string ChangeNote { get; set; }
    public bool IsRevert { get; set; }
}