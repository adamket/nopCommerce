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

    public bool Canceled { get; set; }

    public IList<string> Messages { get; } = new List<string>();

    public IList<string> Errors { get; } = new List<string>();

    public IList<string> SkippedSkuSample { get; } = new List<string>();

    public IList<AkeneoProductImportResult> LoggedItemResults { get; } = new List<AkeneoProductImportResult>();

    public bool Success => !Errors.Any() && FailedCount == 0;

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


    public SyncStatus SyncStatus =>
        Success
            ? SyncStatus.Completed
            : WarningCount > 0
                ? SyncStatus.CompletedWithWarnings
                : SyncStatus.Failed;
}