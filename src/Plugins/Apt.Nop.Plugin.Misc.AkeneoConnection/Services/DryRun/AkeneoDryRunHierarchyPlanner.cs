using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using static Apt.Nop.Plugin.Misc.AkeneoConnection.Services.DryRun.AkeneoDryRunPlanHelper;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services.DryRun;

/// <summary>
/// Surfaces the parent-product and variant-representation decision that occurs
/// above the individual product section synchronizers.
/// </summary>
public sealed class AkeneoDryRunHierarchyPlanner : IAkeneoDryRunSectionPlanner
{
    public int Order => 50;

    public Task PlanAsync(
        AkeneoProductSyncContext context,
        AkeneoProductMappingPreviewModel model,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(model.HierarchyDecision))
            return Task.CompletedTask;

        AddOperation(
            model,
            "Hierarchy and representation",
            "Resolved import path",
            string.IsNullOrWhiteSpace(model.ImmediateParentProductModelCode)
                ? "Akeneo hierarchy"
                : model.ImmediateParentProductModelCode,
            model.CurrentRepresentation,
            model.HierarchyDecision,
            model.HierarchyRequiresReview
                ? AkeneoDryRunOperationType.Review
                : AkeneoDryRunOperationType.NoChange,
            model.ParentProductDecision,
            false);

        return Task.CompletedTask;
    }
}
