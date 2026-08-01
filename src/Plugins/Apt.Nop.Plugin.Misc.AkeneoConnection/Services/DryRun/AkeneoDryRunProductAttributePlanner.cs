using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services.DryRun;

/// <summary>
/// Thin adapter that inserts the real AkeneoProductAttributeSynchronizer read-only plan into
/// the ordered Dry Run pipeline.
/// </summary>
public sealed class AkeneoDryRunProductAttributePlanner(
    AkeneoProductAttributeSynchronizer planProvider)
    : IAkeneoDryRunSectionPlanner
{
    public int Order => 500;

    public Task PlanAsync(
        AkeneoProductSyncContext context,
        AkeneoProductMappingPreviewModel model,
        CancellationToken cancellationToken = default)
    {
        return planProvider.PlanAsync(context, model, cancellationToken);
    }
}
