using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Web.Framework.Models;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Models;

public record AkeneoCategoryMappingListModel : BaseNopModel
{
    public AkeneoCategoryMappingListModel()
    {
        Rows = new List<AkeneoCategoryMappingModel>();
        AvailableNopCategories = new List<SelectListItem>();
    }

    public IList<AkeneoCategoryMappingModel> Rows { get; set; }

    public IList<SelectListItem> AvailableNopCategories { get; set; }
}