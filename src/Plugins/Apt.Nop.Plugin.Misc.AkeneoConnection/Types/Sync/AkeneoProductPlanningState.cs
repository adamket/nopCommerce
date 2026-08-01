namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;

/// <summary>
/// Carries typed, item-scoped values produced by one synchronization section
/// and consumed by a later section while a plan is being built. This avoids
/// coupling cross-section behavior to user-facing Dry Run labels.
/// </summary>
public sealed class AkeneoProductPlanningState
{
    /// <summary>
    /// Final product name intended by the Core section after applying active
    /// mappings, missing-value behavior, and creation defaults.
    /// </summary>
    public string ProposedProductName { get; set; }
}
