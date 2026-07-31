namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;

/// <summary>
/// Explains why a product was selected for a delta reconciliation unit.
/// Multiple reasons can apply, although the batch coordinator normally
/// processes a product only once and keeps the earliest/highest-priority one.
/// </summary>
[Flags]
public enum AkeneoSyncInclusionReason
{
    None = 0,

    /// <summary>
    /// The Akeneo product itself was updated after the delta watermark.
    /// </summary>
    DirectProductChange = 1,

    /// <summary>
    /// An ancestor product model was updated after the delta watermark.
    /// </summary>
    AncestorProductModelChange = 2,

    /// <summary>
    /// The product or one of its ancestor product models was selected only
    /// because a linked asset changed after the delta watermark.
    /// </summary>
    LinkedAssetChange = 4
}
