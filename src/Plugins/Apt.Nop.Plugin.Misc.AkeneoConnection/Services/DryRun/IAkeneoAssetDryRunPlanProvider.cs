using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services.DryRun;

/// <summary>
/// Exposes the asset synchronizer's read-only desired-state comparison to the
/// Dry Run pipeline. The provider is implemented by the real asset
/// synchronizer so asset resolution and comparison rules cannot silently drift
/// into a separate implementation.
/// </summary>
public interface IAkeneoAssetDryRunPlanProvider
    : IAkeneoSectionDryRunPlanProvider
{
}
