namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;

public sealed class AkeneoProductModelDeltaFanOutPlan
{
    public IDictionary<string, AkeneoSyncInclusionReason> AffectedProductModelReasons { get; }
        = new Dictionary<string, AkeneoSyncInclusionReason>(StringComparer.OrdinalIgnoreCase);

    public int DirectChangedProductModelCount { get; set; }

    public int LinkedAssetOnlyProductModelCount { get; set; }

    public int DescendantProductModelCount { get; set; }

    public int ProductModelsRead { get; set; }

    public bool HasAffectedProductModels => AffectedProductModelReasons.Count > 0;
}
