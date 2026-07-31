using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;

public class AkeneoProductImportResult
{
    public int NopProductId { get; set; }

    public int? NopParentProductId { get; set; }

    public int? NopProductAttributeCombinationId { get; set; }

    public int? NopProductAttributeValueId { get; set; }

    public AkeneoProductDestinationKind DestinationKind { get; set; } =
        AkeneoProductDestinationKind.NopProduct;

    public string AkeneoProductUuid { get; set; }

    public string AkeneoProductKey { get; set; }

    public string AkeneoIdentifier { get; set; }

    public string Sku { get; set; }

    public SyncItemActionType ActionType { get; set; } = SyncItemActionType.Skipped;

    public AkeneoSyncInclusionReason InclusionReason { get; set; }

    public IList<string> Messages { get; } = new List<string>();

    public IList<string> Warnings { get; } = new List<string>();

    public IList<string> Errors { get; } = new List<string>();

    public bool Success => !Errors.Any();

    public void AddMessage(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
            Messages.Add(message);
    }

    public void AddWarning(string warning)
    {
        if (!string.IsNullOrWhiteSpace(warning))
            Warnings.Add(warning);
    }

    public void AddError(string error)
    {
        if (!string.IsNullOrWhiteSpace(error))
            Errors.Add(error);
    }
}