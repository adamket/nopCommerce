using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services.Planning;

/// <summary>
/// Internal section plan used by both Dry Run and the real write path.
/// A section builds this plan once; Dry Run renders its preview operations,
/// while synchronization executes the attached actions in the same order.
/// </summary>
internal class AkeneoExecutableSectionPlan
{
    public bool ImportAllowedByProfile { get; set; } = true;

    public string ImportBlockReason { get; set; }

    public IList<AkeneoExecutableSectionOperation> Operations { get; } =
        new List<AkeneoExecutableSectionOperation>();

    public IList<string> Messages { get; } = new List<string>();

    public IList<string> Warnings { get; } = new List<string>();

    public IList<string> CoverageNotes { get; } = new List<string>();

    public void AddOperation(
        string area,
        string target,
        string akeneoSource,
        string currentValue,
        string proposedValue,
        AkeneoDryRunOperationType changeType,
        string detail = null,
        bool isRequired = false,
        Func<CancellationToken, Task<bool>> executeAsync = null)
    {
        Operations.Add(new AkeneoExecutableSectionOperation
        {
            Preview = new AkeneoDryRunOperationPreviewModel
            {
                Area = area,
                Target = target,
                AkeneoSource = akeneoSource,
                CurrentValue = currentValue,
                ProposedValue = proposedValue,
                ChangeType = changeType,
                Detail = detail,
                IsRequired = isRequired
            },
            ExecuteAsync = executeAsync
        });
    }

    public void AddInternalOperation(
        Func<CancellationToken, Task<bool>> executeAsync)
    {
        if (executeAsync == null)
            return;

        Operations.Add(new AkeneoExecutableSectionOperation
        {
            ExecuteAsync = executeAsync
        });
    }

    public void AddReview(
        string area,
        string target,
        string akeneoSource,
        string detail,
        bool isRequired = false,
        string currentValue = null,
        string proposedValue = null)
    {
        AddOperation(
            area,
            target,
            akeneoSource,
            currentValue,
            proposedValue,
            AkeneoDryRunOperationType.Review,
            detail,
            isRequired);

        if (!string.IsNullOrWhiteSpace(detail))
            Warnings.Add(detail);
    }

    public void Render(AkeneoProductMappingPreviewModel model)
    {
        foreach (var operation in Operations.Where(item => item.Preview != null))
        {
            operation.Preview.DisplayOrder = model.Operations.Count + 1;
            model.Operations.Add(operation.Preview);
        }

        foreach (var message in Messages.Where(value => !string.IsNullOrWhiteSpace(value)))
            model.Messages.Add(message);

        foreach (var warning in Warnings.Where(value => !string.IsNullOrWhiteSpace(value)))
            model.Warnings.Add(warning);

        foreach (var note in CoverageNotes.Where(value => !string.IsNullOrWhiteSpace(value)))
            model.CoverageNotes.Add(note);
    }

    public async Task<bool> ExecuteAsync(
        AkeneoProductSyncContext context,
        CancellationToken cancellationToken = default)
    {
        foreach (var message in Messages.Where(value => !string.IsNullOrWhiteSpace(value)))
            context.Result.AddMessage(message);

        foreach (var warning in Warnings.Where(value => !string.IsNullOrWhiteSpace(value)))
            context.Result.AddWarning(warning);

        var changed = false;

        foreach (var operation in Operations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (operation.ExecuteAsync != null)
                changed |= await operation.ExecuteAsync(cancellationToken);
        }

        if (changed)
            context.MarkChanged();

        return changed;
    }
}

internal sealed class AkeneoExecutableSectionOperation
{
    public AkeneoDryRunOperationPreviewModel Preview { get; init; }

    public Func<CancellationToken, Task<bool>> ExecuteAsync { get; init; }
}
