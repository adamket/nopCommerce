using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

/// <summary>
/// Read-only planning contract owned by a real synchronization section.
/// Implementations must use the same resolution/comparison rules that their
/// write path uses so Dry Run cannot silently drift into a parallel behavior.
/// </summary>
public interface IAkeneoSectionDryRunPlanProvider
{
    int Order { get; }

    Task PlanAsync(
        AkeneoProductSyncContext context,
        AkeneoProductMappingPreviewModel model,
        CancellationToken cancellationToken = default);
}
