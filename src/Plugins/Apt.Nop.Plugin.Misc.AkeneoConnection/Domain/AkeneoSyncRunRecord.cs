using Nop.Core;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;

public class AkeneoSyncRunRecord : BaseEntity
{
    public int? SyncProfileId { get; set; }

    public int SyncTypeId { get; set; }

    public int RunModeId { get; set; }

    public int SyncStatusId { get; set; }

    public DateTime StartedOnUtc { get; set; }

    /// <summary>
    /// Boundary captured before reading Akeneo. A later delta run should use
    /// this value, with a small overlap, rather than FinishedOnUtc.
    /// </summary>
    public DateTime WatermarkUtc { get; set; }

    public DateTime? FinishedOnUtc { get; set; }

    public int TotalRead { get; set; }

    public int CreatedCount { get; set; }

    public int UpdatedCount { get; set; }

    public int SkippedCount { get; set; }

    public int FailedCount { get; set; }

    public int WarningCount { get; set; }

    public bool CompletedAllPages { get; set; }

    public bool WasTruncated { get; set; }

    public bool ReconciliationCompleted { get; set; }

    public string ScopeHash { get; set; }

    public string SearchJsonSnapshot { get; set; }

    public string ProfileSnapshotJson { get; set; }

    public string ErrorSummary { get; set; }
}

public enum SyncType
{
    InitialImport = 10,
    DeltaSync = 20,
    ManualProductSync = 30,
    ManualProfileSync = 40,
    ManualFullProfileSync = 50,
    ScheduledFullSync = 60
}

public enum AkeneoRunMode
{
    Full = 10,
    Delta = 20,
    SingleProduct = 30
}

public enum SyncStatus
{
    Started = 10,
    Completed = 20,
    CompletedWithWarnings = 30,
    Failed = 40,
    Cancelled = 50,
    CompletedWithErrors = 60
}
