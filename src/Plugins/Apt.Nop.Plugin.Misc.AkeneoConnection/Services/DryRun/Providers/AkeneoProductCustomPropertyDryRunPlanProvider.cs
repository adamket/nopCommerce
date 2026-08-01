using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services.Planning;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using static Apt.Nop.Plugin.Misc.AkeneoConnection.Services.DryRun.AkeneoDryRunPlanHelper;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public partial class AkeneoProductCustomPropertySynchronizer
{
    public async Task PlanAsync(
        AkeneoProductSyncContext context,
        AkeneoProductMappingPreviewModel model,
        CancellationToken cancellationToken = default)
    {
        var plan = await BuildPlanAsync(context, cancellationToken);
        plan.Render(model);
    }

    private async Task<AkeneoExecutableSectionPlan> BuildPlanAsync(
        AkeneoProductSyncContext context,
        CancellationToken cancellationToken)
    {
        var plan = new AkeneoExecutableSectionPlan();

        foreach (var mapped in context.GetMappings(NopTargetType.CustomProperty))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var key = mapped.Mapping.NopTargetKey?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                var attributeCode = mapped.Mapping.AkeneoAttributeCode?.Trim();
                key = string.IsNullOrWhiteSpace(attributeCode)
                    ? null
                    : $"Apt.Akeneo.CustomProperty.{attributeCode}";
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                plan.AddReview(
                    "Custom properties",
                    "Missing key",
                    GetSourceName(mapped),
                    "The custom-property mapping has no target key or Akeneo attribute code.",
                    mapped.Mapping.IsRequired);
                continue;
            }

            var current = context.ExistingProduct == null
                ? null
                : await _genericAttributeService.GetAttributeAsync<string>(
                    context.ExistingProduct,
                    key) ?? string.Empty;

            if (!mapped.HasValue &&
                context.Request.CustomPropertyMissingValueBehavior ==
                    AkeneoMissingValueBehavior.PreserveExisting)
            {
                plan.AddOperation(
                    "Custom properties",
                    key,
                    GetSourceName(mapped),
                    current,
                    current,
                    AkeneoDryRunOperationType.Preserve,
                    "The mapping produced no value and the profile preserves existing custom properties.",
                    mapped.Mapping.IsRequired);
                continue;
            }

            var proposed = !mapped.HasValue
                ? string.Empty
                : mapped.Value?.DisplayValues is { Count: > 1 } values
                    ? JsonSerializer.Serialize(values)
                    : mapped.DisplayValue?.Trim() ?? string.Empty;

            var type = DetermineScalarChangeType(
                current,
                proposed,
                ignoreCase: true);

            if (context.ExistingProduct == null && mapped.HasValue &&
                !string.IsNullOrEmpty(proposed))
            {
                type = AkeneoDryRunOperationType.Add;
            }

            Func<CancellationToken, Task<bool>> executeAsync = null;
            if (type is AkeneoDryRunOperationType.Add or
                AkeneoDryRunOperationType.Update or
                AkeneoDryRunOperationType.Clear)
            {
                var targetKey = key;
                var valueToSave = proposed;
                var operationType = type;

                executeAsync = async _ =>
                {
                    if (context.Product == null)
                        return false;

                    if (operationType == AkeneoDryRunOperationType.Clear)
                    {
                        await _genericAttributeService.SaveAttributeAsync<string>(
                            context.Product,
                            targetKey,
                            null);

                        context.Result.AddMessage(
                            $"Cleared custom property '{targetKey}'.");
                        return true;
                    }

                    await _genericAttributeService.SaveAttributeAsync(
                        context.Product,
                        targetKey,
                        valueToSave);

                    context.Result.AddMessage(
                        $"Set custom property '{targetKey}' for product ID {context.Product.Id}.");
                    return true;
                };
            }

            plan.AddOperation(
                "Custom properties",
                key,
                GetSourceName(mapped),
                current,
                proposed,
                type,
                !mapped.HasValue && type == AkeneoDryRunOperationType.Clear
                    ? "Saving a null value deletes the nopCommerce generic-attribute record."
                    : null,
                mapped.Mapping.IsRequired,
                executeAsync);
        }

        return plan;
    }
}
