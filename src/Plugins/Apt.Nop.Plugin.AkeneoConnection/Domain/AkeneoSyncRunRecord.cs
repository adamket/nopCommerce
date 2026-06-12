using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Apt.Nop.Plugin.AkeneoConnection.Domain;
public class AkeneoSyncRunRecord
{
    public int SyncTypeId { get; set; }
    public DateTime StartedOnUtc { get; set; }
    public DateTime FinishedOnUtc { get; set; }
    public string Status { get; set; }// ??
    public int TotalRead { get; set; }
    public int CreatedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int SkippedCount { get; set; }
    public int FailedCount { get; set; }
    public string ErrorSummary { get; set; }
}

public enum SyncType
{
    InitialImport = 10,
    DeltaSync = 20,
    ManualProductSync = 30
}