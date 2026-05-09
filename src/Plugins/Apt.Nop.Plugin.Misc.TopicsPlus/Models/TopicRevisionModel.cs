using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Apt.Nop.Plugin.Misc.TopicsPlus.Models;

public record TopicRevisionModel : BaseNopEntityModel
{
    public int TopicId { get; set; }

    public int Version { get; set; }

    public string Title { get; set; }

    public string Body { get; set; }

    public string CustomerName { get; set; }

    public int CustomerId { get; set; }

    public string CreatedOn { get; set; }

    public string ChangeNote { get; set; }
    public bool IsRevert { get; set; }

    public bool HideRevertButton { get; set; }
}