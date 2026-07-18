using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;

public class AkeneoProductBatchImportResult
{
    public int SyncRunRecordId { get; set; }

    public int TotalRead { get; set; }

    public int CreatedCount { get; set; }

    public int UpdatedCount { get; set; }

    public int SkippedCount { get; set; }

    public int FailedCount { get; set; }

    public int WarningCount { get; set; }

    public int ReconciledCount { get; set; }

    public bool Canceled { get; set; }

    public bool CompletedAllPages { get; set; }

    public bool WasTruncated { get; set; }

    public bool ReconciliationCompleted { get; set; }

    public IList<string> Messages { get; } = new List<string>();

    public IList<string> Errors { get; } = new List<string>();

    public IList<string> SkippedSkuSample { get; } = new List<string>();

    public IList<AkeneoProductImportResult> LoggedItemResults { get; } = new List<AkeneoProductImportResult>();

    public bool Success => !Canceled && !Errors.Any() && FailedCount == 0;

    public bool IsAuthoritative =>
        Success && CompletedAllPages && !WasTruncated;

    public void AddMessage(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
            Messages.Add(message);
    }

    public void AddError(string error)
    {
        if (!string.IsNullOrWhiteSpace(error))
            Errors.Add(error);
    }

    public SyncStatus SyncStatus
    {
        get
        {
            if (Canceled)
                return SyncStatus.Cancelled;

            if (Errors.Any() || FailedCount > 0)
                return SyncStatus.CompletedWithErrors;

            if (WarningCount > 0)
                return SyncStatus.CompletedWithWarnings;

            return SyncStatus.Completed;
        }
    }
}
