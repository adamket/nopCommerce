using Nop.Web.Framework.Models;

namespace Apt.Nop.Plugin.Misc.TopicsPlus.Models;

public record TopicRevisionSearchModel : BaseSearchModel
{
    public int SearchTopicId { get; set; }
}
