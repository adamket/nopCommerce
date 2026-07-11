using Nop.Web.Framework.Models;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Models;

public record AkeneoFamilyMappingListModel : BaseNopModel
{
    public IList<AkeneoFamilyMappingModel> Configurations { get; set; } =
        new List<AkeneoFamilyMappingModel>();
}
