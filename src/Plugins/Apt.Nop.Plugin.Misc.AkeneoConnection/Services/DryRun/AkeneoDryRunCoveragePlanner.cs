using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using static Apt.Nop.Plugin.Misc.AkeneoConnection.Services.DryRun.AkeneoDryRunPlanHelper;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services.DryRun;

/// <summary>
/// Reports the remaining known coverage boundaries after all concrete section
/// planners have run. Assets and hierarchy/representation now have dedicated
/// planners and are therefore not silently represented as generic omissions.
/// </summary>
public sealed class AkeneoDryRunCoveragePlanner : IAkeneoDryRunSectionPlanner
{
    public int Order => 900;

    public Task PlanAsync(
        AkeneoProductSyncContext context,
        AkeneoProductMappingPreviewModel model,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        AddUnsupportedTargetOperations(context, model);

        if (context.ExistingSyncState != null &&
            (AkeneoProductLifecycleStatus)context.ExistingSyncState.LifecycleStatusId !=
                AkeneoProductLifecycleStatus.Active)
        {
            model.CoverageNotes.Add(
                "The existing source binding has a non-active lifecycle status. User-visible flag restorations are itemized above; a successful import also returns the internal binding to Active.");
        }

        if (!string.IsNullOrWhiteSpace(model.HierarchyDecision) &&
            model.HierarchyRequiresReview)
        {
            model.CoverageNotes.Add(
                "The hierarchy planner resolved the expected parent and representation as far as the current data allows, but the marked item still depends on import-time structure checks or parent-side writes.");
        }

        model.CoverageNotes.Add(
            "The import rebuilds the plan against the then-current nopCommerce state. Changes made after this preview can alter the final result.");

        return Task.CompletedTask;
    }

    private static void AddUnsupportedTargetOperations(
        AkeneoProductSyncContext context,
        AkeneoProductMappingPreviewModel model)
    {
        foreach (var mapped in context.MappedValues.Where(value =>
                     value.TargetType == NopTargetType.Manufacturer))
        {
            AddReviewOperation(
                model,
                "Manufacturer",
                mapped.Mapping.NopTargetKey ?? "Manufacturer",
                GetSourceName(mapped),
                "Manufacturer synchronization is not implemented by the current product sync pipeline.",
                mapped.Mapping.IsRequired,
                null,
                mapped.DisplayValue);
        }
    }
}
