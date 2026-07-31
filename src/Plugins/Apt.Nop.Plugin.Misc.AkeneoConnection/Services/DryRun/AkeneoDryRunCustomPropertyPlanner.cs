using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Services.Common;
using static Apt.Nop.Plugin.Misc.AkeneoConnection.Services.DryRun.AkeneoDryRunPlanHelper;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services.DryRun;

public sealed class AkeneoDryRunCustomPropertyPlanner(
    IGenericAttributeService genericAttributeService) : IAkeneoDryRunSectionPlanner
{
    public int Order => 600;

    public async Task PlanAsync(
        AkeneoProductSyncContext context,
        AkeneoProductMappingPreviewModel model,
        CancellationToken cancellationToken = default)
    {
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
                AddReviewOperation(
                    model,
                    "Custom properties",
                    "Missing key",
                    GetSourceName(mapped),
                    "The custom-property mapping has no target key or Akeneo attribute code.",
                    mapped.Mapping.IsRequired);
                continue;
            }

            var current = context.ExistingProduct == null
                ? null
                : await genericAttributeService.GetAttributeAsync<string>(
                    context.ExistingProduct,
                    key) ?? string.Empty;

            if (!mapped.HasValue &&
                context.Request.CustomPropertyMissingValueBehavior ==
                    AkeneoMissingValueBehavior.PreserveExisting)
            {
                AddOperation(
                    model,
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

            AddOperation(
                model,
                "Custom properties",
                key,
                GetSourceName(mapped),
                current,
                proposed,
                type,
                !mapped.HasValue && type == AkeneoDryRunOperationType.Clear
                    ? "Saving a null value deletes the nopCommerce generic-attribute record."
                    : null,
                mapped.Mapping.IsRequired);
        }
    }
}
