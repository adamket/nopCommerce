using Nop.Core;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
public class AkeneoSyncItemLog : BaseEntity
{
    public string SyncRunId { get; set; }
    public string AkeneoProductUuid { get; set; }
    public string AkeneoIdentifier { get; set; }
    public int NopProductId { get; set; }
    public int ActionTypeId { get; set; }
    public string Message { get; set; }
    public string RawPayloadSnapshot { get; set; } //TODO remove?
    public DateTime CreatedOnUtc { get; set; }
}

public enum ActionType
{
    Created = 10,
    Updated = 20,
    Skipped = 30,
    Failed = 40
}