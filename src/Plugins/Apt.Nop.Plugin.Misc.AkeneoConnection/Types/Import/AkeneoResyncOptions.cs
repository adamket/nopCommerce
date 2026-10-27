namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;

/// <summary>
/// Choices for a manual resync, which reads every product in the profile's
/// scope but never removes products or moves the delta watermark.
/// </summary>
public class AkeneoResyncOptions
{
    /// <summary>
    /// Writes every product again, even when its Akeneo output is unchanged,
    /// so edits made in nopCommerce are put back. Off, unchanged products are
    /// skipped and only products whose output changed under the current
    /// mappings are written.
    /// </summary>
    public bool RepairDestination { get; set; }

    /// <summary>Updates existing products only; nothing new is created.</summary>
    public bool UpdateExistingOnly { get; set; }

    /// <summary>
    /// Narrows the run to these families. They must be within the profile's
    /// families when the profile has any. Empty means the whole profile.
    /// </summary>
    public IList<string> FamilyCodes { get; set; } = new List<string>();
}
