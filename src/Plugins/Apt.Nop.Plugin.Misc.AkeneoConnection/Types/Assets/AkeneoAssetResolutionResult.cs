namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Assets;

/// <summary>
/// Separates an authoritative empty asset collection from a source value that
/// could not be selected for the requested locale/channel. Reconciliation is
/// intentionally skipped for the latter to avoid destructive media removal.
/// </summary>
public sealed class AkeneoAssetResolutionResult
{
    public bool CanReconcile { get; init; }
    public string Warning { get; init; }
    public IReadOnlyList<AkeneoResolvedAsset> Assets { get; init; }
        = Array.Empty<AkeneoResolvedAsset>();
}
