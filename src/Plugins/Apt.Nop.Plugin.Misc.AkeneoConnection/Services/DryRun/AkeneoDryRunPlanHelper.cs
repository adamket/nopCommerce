using System.Xml.Linq;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core.Domain.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services.DryRun;

internal static class AkeneoDryRunPlanHelper
{
    public static void AddReviewOperation(
        AkeneoProductMappingPreviewModel model,
        string area,
        string target,
        string source,
        string detail,
        bool required = false,
        string current = null,
        string proposed = null)
    {
        AddOperation(
            model,
            area,
            target,
            source,
            current,
            proposed,
            AkeneoDryRunOperationType.Review,
            detail,
            required);
    }

    public static void AddOperation(
        AkeneoProductMappingPreviewModel model,
        string area,
        string target,
        string source,
        string current,
        string proposed,
        AkeneoDryRunOperationType type,
        string detail = null,
        bool required = false)
    {
        model.Operations.Add(new AkeneoDryRunOperationPreviewModel
        {
            DisplayOrder = model.Operations.Count + 1,
            Area = area,
            Target = target,
            AkeneoSource = source,
            CurrentValue = current,
            ProposedValue = proposed,
            ChangeType = type,
            Detail = detail,
            IsRequired = required
        });
    }

    public static AkeneoDryRunOperationType DetermineScalarChangeType(
        string current,
        string proposed,
        bool ignoreCase = false)
    {
        var comparison = ignoreCase
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var currentValue = current ?? string.Empty;
        var proposedValue = proposed ?? string.Empty;

        if (string.Equals(currentValue, proposedValue, comparison))
            return AkeneoDryRunOperationType.NoChange;

        if (currentValue.Length > 0 && proposedValue.Length == 0)
            return AkeneoDryRunOperationType.Clear;

        return AkeneoDryRunOperationType.Update;
    }

    /// <summary>
    /// Compares raw scalar values without normalizing null and empty strings.
    /// Use this when the write path's setter distinguishes null from empty.
    /// </summary>
    public static AkeneoDryRunOperationType DetermineExactScalarChangeType(
        string current,
        string proposed,
        bool ignoreCase = false)
    {
        var comparison = ignoreCase
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (string.Equals(current, proposed, comparison))
            return AkeneoDryRunOperationType.NoChange;

        if (!string.IsNullOrEmpty(current) && string.IsNullOrEmpty(proposed))
            return AkeneoDryRunOperationType.Clear;

        return AkeneoDryRunOperationType.Update;
    }

    public static string GetSourceName(AkeneoResolvedMappedValue mapped)
    {
        return !string.IsNullOrWhiteSpace(mapped.ResolvedSourceDisplayName)
            ? mapped.ResolvedSourceDisplayName
            : AkeneoMappingHelper.GetSourceDisplayName(mapped.Mapping);
    }

    public static string GetProposedProductName(
        AkeneoProductSyncContext context,
        AkeneoProductMappingPreviewModel model)
    {
        var nameOperation = model?.Operations
            .LastOrDefault(operation =>
                string.Equals(operation.Area, "Product fields", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(operation.Target, "Name", StringComparison.OrdinalIgnoreCase));

        return !string.IsNullOrWhiteSpace(nameOperation?.ProposedValue)
            ? nameOperation.ProposedValue
            : context.Product?.Name ??
              context.ExistingProduct?.Name ??
              context.Sku ??
              context.SourceCode ??
              context.ProductKey;
    }

    public static bool TryParseBoolean(string value, out bool parsed)
    {
        if (bool.TryParse(value, out parsed))
            return true;

        if (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "y", StringComparison.OrdinalIgnoreCase))
        {
            parsed = true;
            return true;
        }

        if (string.Equals(value, "0", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "no", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "n", StringComparison.OrdinalIgnoreCase))
        {
            parsed = false;
            return true;
        }

        parsed = false;
        return false;
    }

    public static bool IsValueUsedByCombination(
        int productAttributeValueId,
        IEnumerable<ProductAttributeCombination> combinations)
    {
        foreach (var combination in combinations)
        {
            if (string.IsNullOrWhiteSpace(combination.AttributesXml))
                continue;

            try
            {
                var document = XDocument.Parse(combination.AttributesXml);
                if (document.Descendants("Value").Any(element =>
                        int.TryParse(element.Value, out var valueId) &&
                        valueId == productAttributeValueId))
                {
                    return true;
                }
            }
            catch
            {
                // Match the write pipeline's conservative behavior: malformed
                // combination XML means the value must be treated as in use.
                return true;
            }
        }

        return false;
    }
}
