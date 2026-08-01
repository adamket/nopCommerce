using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services.DryRun;

/// <summary>
/// Adapter that inserts the asset synchronizer's own read-only plan into the
/// ordered Dry Run pipeline.
/// </summary>
public sealed class AkeneoDryRunAssetPlanner(
    IAkeneoAssetDryRunPlanProvider assetPlanProvider)
    : IAkeneoDryRunSectionPlanner
{
    public int Order => 550;

    public Task PlanAsync(
        AkeneoProductSyncContext context,
        AkeneoProductMappingPreviewModel model,
        CancellationToken cancellationToken = default)
    {
        return assetPlanProvider.PlanAsync(context, model, cancellationToken);
    }
}
