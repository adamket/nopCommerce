using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;

public class AkeneoProductImportExecutionResult
{
    public bool Success { get; set; }

    public bool Canceled { get; set; }

    public bool CompletedWithErrors { get; set; }

    public bool ProfileNotFound { get; set; }

    public bool ProfileDisabled { get; set; }

    public int? ProfileId { get; set; }

    public int? SyncRunRecordId { get; set; }

    public SyncStatus SyncStatus { get; set; }

    public string Message { get; set; }

    public int TotalRead { get; set; }

    public int CreatedCount { get; set; }

    public int UpdatedCount { get; set; }

    public int SkippedCount { get; set; }

    public int FailedCount { get; set; }

    public int WarningCount { get; set; }

    public IList<string> Errors { get; set; } = new List<string>();

    public IList<string> Messages { get; set; } = new List<string>();
}