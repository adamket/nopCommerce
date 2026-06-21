using Nop.Web.Framework.Models;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Models.SyncProfiles;

public record AkeneoSyncProfileListModel : BaseNopModel
{
    public IList<AkeneoSyncProfileModel> Profiles { get; set; } = new List<AkeneoSyncProfileModel>();
}