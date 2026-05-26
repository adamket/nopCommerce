using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Web.Framework.Models;

namespace Apt.Nop.Plugin.Misc.TopicsPlus.Models;
public record TopicDataModel : BaseNopEntityModel
{
    public int TopicId { get; set; }
    public IList<string> SelectedWidgetZones { get; set; }
    public IList<SelectListItem> AvailableWidgetZones { get; set; }
}
