using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;

/// <summary>
/// Shared evaluator for family submodel override rules. Both the real import
/// orchestrator and Dry Run use this helper so rule matching cannot drift.
/// </summary>
public static class AkeneoSubModelRuleMatcher
{
    public static AkeneoFamilySubModelRule FindMatch(
        IEnumerable<AkeneoFamilySubModelRule> rules,
        AkeneoProductDefinition leaf,
        AkeneoProductDefinition subModel)
    {
        return rules?.FirstOrDefault(rule => Matches(rule, leaf, subModel));
    }

    public static bool Matches(
        AkeneoFamilySubModelRule rule,
        AkeneoProductDefinition leaf,
        AkeneoProductDefinition subModel)
    {
        if (rule == null)
            return false;

        var hasSubModelCondition =
            !string.IsNullOrWhiteSpace(rule.AkeneoAxisAttributeCode);
        var hasVariantCondition =
            !string.IsNullOrWhiteSpace(rule.VariantAxisAttributeCode);

        if (!hasSubModelCondition && !hasVariantCondition)
            return false;

        if (hasSubModelCondition &&
            !ValueMatches(
                GetAttributeValue(subModel, rule.AkeneoAxisAttributeCode),
                rule.TriggerValue))
        {
            return false;
        }

        return !hasVariantCondition || ValueMatches(
            GetAttributeValue(leaf, rule.VariantAxisAttributeCode),
            rule.VariantTriggerValue);
    }

    private static bool ValueMatches(string actual, string trigger) =>
        !string.IsNullOrWhiteSpace(actual) &&
        string.Equals(
            actual.Trim(),
            trigger?.Trim(),
            StringComparison.OrdinalIgnoreCase);

    private static string GetAttributeValue(
        AkeneoProductDefinition item,
        string attributeCode)
    {
        if (string.IsNullOrWhiteSpace(attributeCode) ||
            item?.Values.ValueKind != JsonValueKind.Object ||
            !item.Values.TryGetProperty(attributeCode, out var entries) ||
            entries.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("data", out var data))
                continue;

            return data.ValueKind switch
            {
                JsonValueKind.String => data.GetString(),
                JsonValueKind.Number => data.GetRawText(),
                _ => null
            };
        }

        return null;
    }
}
