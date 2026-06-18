using Nop.Web.Framework.Models;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Models;

public record AkeneoSyncRunRecordModel : BaseNopEntityModel
{
    public string SyncRunRecordId { get; set; }
    public string SyncType { get; set; }
    public string Status { get; set; }
    public DateTime StartedOnUtc { get; set; }
    public DateTime? FinishedOnUtc { get; set; }
    public int TotalRead { get; set; }
    public int CreatedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int SkippedCount { get; set; }
    public int FailedCount { get; set; }
    public string ErrorSummary { get; set; }
}