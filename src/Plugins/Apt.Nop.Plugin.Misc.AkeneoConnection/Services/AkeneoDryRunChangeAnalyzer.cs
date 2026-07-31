using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services.DryRun;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public sealed class AkeneoDryRunChangeAnalyzer(
    IEnumerable<IAkeneoDryRunSectionPlanner> sectionPlanners)
    : IAkeneoDryRunChangeAnalyzer
{
    private readonly IReadOnlyList<IAkeneoDryRunSectionPlanner> _sectionPlanners =
        sectionPlanners.OrderBy(planner => planner.Order).ToList();

    public async Task AnalyzeAsync(
        AkeneoProductSyncContext context,
        AkeneoProductMappingPreviewModel model,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(model);

        model.Operations.Clear();

        foreach (var planner in _sectionPlanners)
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
                    $"Dry-run planning failed in {planner.GetType().Name}: {ex.Message}");
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
}
