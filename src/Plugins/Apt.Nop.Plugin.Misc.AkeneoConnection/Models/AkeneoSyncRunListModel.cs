using Nop.Web.Framework.Models;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
public record AkeneoSyncRunListModel : BaseNopModel
{
    public int SyncRunRecordPageSize { get; set; }
    public int SyncItemLogPageSize { get; set; }
    public IList<AkeneoSyncRunRecordModel> Runs { get; set; } = new List<AkeneoSyncRunRecordModel>();
}
