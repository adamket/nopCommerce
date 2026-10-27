using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Categories;

/// <summary>A category content preview or sync. <see cref="Applied"/> is false for a preview.</summary>
public sealed class AkeneoCategoryContentRunResult
{
    public bool Success { get; set; } = true;

    public bool Applied { get; set; }

    public bool AlreadyRunning { get; set; }

    public string Message { get; set; }

    public int? SyncRunRecordId { get; set; }

    public List<AkeneoCategoryContentItemResult> Categories { get; } = new();

    public int UpdatedCount => Categories.Count(item => item.Status == AkeneoCategoryContentStatus.Updated);

    public int UnchangedCount => Categories.Count(item => item.Status == AkeneoCategoryContentStatus.Unchanged);

    public int SkippedCount => Categories.Count(item => item.Status == AkeneoCategoryContentStatus.Skipped);

    public int FailedCount => Categories.Count(item => item.Status == AkeneoCategoryContentStatus.Failed);

    public int WarningCount => Categories.Sum(item => item.Warnings.Count);
}

/// <summary>
/// One mapped category. In a preview, <see cref="AkeneoCategoryContentStatus.Updated"/>
/// means the sync would change it.
/// </summary>
public sealed class AkeneoCategoryContentItemResult
{
    public string AkeneoCode { get; set; }

    public string AkeneoLabel { get; set; }

    public int NopCategoryId { get; set; }

    public string NopCategoryName { get; set; }

    public AkeneoCategoryContentStatus Status { get; set; } = AkeneoCategoryContentStatus.Unchanged;

    public List<AkeneoCategoryContentChange> Changes { get; } = new();

    public List<string> Errors { get; } = new();

    public List<string> Warnings { get; } = new();
}

/// <summary>One property change, with both values shortened for display.</summary>
public sealed record AkeneoCategoryContentChange(
    string Property,
    string Language,
    string From,
    string To);
