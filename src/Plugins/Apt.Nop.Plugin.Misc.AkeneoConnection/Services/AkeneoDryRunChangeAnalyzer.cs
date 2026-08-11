using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services.DryRun;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

/// <summary>
/// Builds Dry Run from the exact plan providers owned by the registered write
/// synchronizers, plus the small number of supplemental planners that describe
/// behavior above/beyond individual write sections (hierarchy and coverage).
/// This makes analyzer/write parity an enforced registration invariant instead
/// of relying on a parallel list of adapter registrations.
/// </summary>
public sealed class AkeneoDryRunChangeAnalyzer
    : IAkeneoDryRunChangeAnalyzer
{
    private readonly IReadOnlyList<PlannerInvocation> _planners;

    /// <summary>
    /// Compatibility constructor for focused unit tests or callers that supply
    /// a complete planner list directly. Production DI uses the synchronizer +
    /// supplemental-planner constructor below so write/analyzer parity is
    /// enforced automatically.
    /// </summary>
    public AkeneoDryRunChangeAnalyzer(
        IEnumerable<IAkeneoDryRunSectionPlanner> sectionPlanners)
    {
        _planners = sectionPlanners
            .Select(planner => new PlannerInvocation(
                planner.Order,
                planner.GetType().Name,
                planner.PlanAsync))
            .OrderBy(planner => planner.Order)
            .ThenBy(planner => planner.Name, StringComparer.Ordinal)
            .ToList();
    }

    public AkeneoDryRunChangeAnalyzer(
        IEnumerable<IAkeneoProductSectionSynchronizer> synchronizers,
        IEnumerable<IAkeneoDryRunSectionPlanner> supplementalPlanners)
    {
        var planners = new List<PlannerInvocation>();

        foreach (var synchronizer in synchronizers)
        {
            if (synchronizer is not IAkeneoSectionDryRunPlanProvider planProvider)
            {
                throw new InvalidOperationException(
                    $"Synchronization section '{synchronizer.GetType().Name}' " +
                    "does not implement IAkeneoSectionDryRunPlanProvider. " +
                    "Every destination-writing section must expose its read-only " +
                    "plan so desired-state hashing cannot silently diverge from writes.");
            }

            planners.Add(new PlannerInvocation(
                planProvider.Order,
                synchronizer.GetType().Name,
                planProvider.PlanAsync));
        }

        foreach (var planner in supplementalPlanners)
        {
            planners.Add(new PlannerInvocation(
                planner.Order,
                planner.GetType().Name,
                planner.PlanAsync));
        }

        _planners = planners
            .OrderBy(planner => planner.Order)
            .ThenBy(planner => planner.Name, StringComparer.Ordinal)
            .ToList();
    }

    public async Task AnalyzeAsync(
        AkeneoProductSyncContext context,
        AkeneoProductMappingPreviewModel model,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(model);

        model.Operations.Clear();

        foreach (var planner in _planners)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await planner.PlanAsync(context, model, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                model.Errors.Add(
                    $"Dry-run planning failed in {planner.Name}: {ex.Message}");
            }
        }

        model.Operations = model.Operations
            .OrderBy(operation => operation.DisplayOrder)
            .ThenBy(operation => operation.Area, StringComparer.OrdinalIgnoreCase)
            .ThenBy(operation => operation.Target, StringComparer.OrdinalIgnoreCase)
            .ToList();

        model.Action = BuildAction(model, context.ExistingProduct == null);
    }

    private static string BuildAction(
        AkeneoProductMappingPreviewModel model,
        bool isNew)
    {
        if (!model.ImportAllowedByProfile)
            return model.ImportBlockReason;

        if (model.Errors.Any())
        {
            return "The dry run found blocking errors, so one-time import is disabled.";
        }

        if (isNew)
        {
            return $"Would create a nopCommerce product with {model.PlannedChangeCount} planned operation(s)."
                   + (model.ReviewCount > 0
                       ? $" {model.ReviewCount} item(s) require review."
                       : string.Empty);
        }

        if (model.PlannedChangeCount == 0)
        {
            return model.ReviewCount > 0
                ? $"No changes were found, but {model.ReviewCount} item(s) require review."
                : "No mapped changes were detected for the existing nopCommerce product.";
        }

        return $"Would apply {model.PlannedChangeCount} change(s) to the existing nopCommerce product."
               + (model.ReviewCount > 0
                   ? $" {model.ReviewCount} item(s) require review."
                   : string.Empty);
    }

    private sealed record PlannerInvocation(
        int Order,
        string Name,
        Func<AkeneoProductSyncContext, AkeneoProductMappingPreviewModel,
            CancellationToken, Task> PlanAsync);
}
