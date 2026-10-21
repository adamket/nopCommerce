using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

/// <summary>
/// Resolves Akeneo select-option labels the same way for every section that names nopCommerce
/// entities after them (specification options, product tags).
/// </summary>
internal static class AkeneoOptionLabels
{
    /// <summary>Whether a mapped value is a simple or multi select: the payload's attribute_type when present, else the mapping's stored attribute type.</summary>
    public static bool IsOptionSource(AkeneoResolvedMappedValue mapped) =>
        !string.IsNullOrWhiteSpace(mapped.Value?.AttributeCode) &&
        (string.IsNullOrWhiteSpace(mapped.Value.SourceAttributeType)
            ? (AkeneoAttributeType)mapped.Mapping.AkeneoAttributeTypeId is
                AkeneoAttributeType.Select or AkeneoAttributeType.MultiSelect
            : mapped.Value.SourceAttributeType is "pim_catalog_simpleselect" or "pim_catalog_multiselect");

    /// <summary>
    /// The option's Akeneo label: the request locale, then en_US, then any locale. Null when
    /// the option has no label at all; the code is never substituted. The attribute's option
    /// list is authoritative and read once per attribute into <paramref name="cache"/>; without
    /// an API client the product payload's linked_data label is used, and only when it is a real
    /// label rather than the code it falls back to.
    /// </summary>
    public static async Task<string> ResolveAsync(
        IAkeneoApiClient akeneoApiClient,
        IDictionary<string, Dictionary<string, AkeneoAttributeOptionDefinition>> cache,
        string attributeCode,
        AkeneoResolvedOptionItem optionItem,
        string locale,
        CancellationToken cancellationToken)
    {
        var code = optionItem.AkeneoOptionCode?.Trim();
        if (string.IsNullOrWhiteSpace(code))
            return null;

        if (akeneoApiClient == null)
        {
            var payloadLabel = optionItem.DisplayName?.Trim();
            return string.IsNullOrWhiteSpace(payloadLabel) ||
                   string.Equals(payloadLabel, code, StringComparison.Ordinal)
                ? null
                : payloadLabel;
        }

        if (!cache.TryGetValue(attributeCode, out var options))
        {
            options = (await akeneoApiClient.GetAttributeOptionDefinitionsAsync(
                    attributeCode,
                    cancellationToken: cancellationToken))
                .Where(option => !string.IsNullOrWhiteSpace(option.Code))
                .GroupBy(option => option.Code.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            cache[attributeCode] = options;
        }

        return options.TryGetValue(code, out var option) ? GetLabelOnly(option, locale) : null;
    }

    public static string GetLabelOnly(AkeneoAttributeOptionDefinition option, string locale)
    {
        var labels = option?.Labels;
        if (labels == null || labels.Count == 0)
            return null;

        foreach (var key in new[] { locale?.Trim(), "en_US" })
        {
            if (!string.IsNullOrWhiteSpace(key) &&
                labels.TryGetValue(key, out var label) &&
                !string.IsNullOrWhiteSpace(label))
            {
                return label.Trim();
            }
        }

        return labels.Values.FirstOrDefault(label => !string.IsNullOrWhiteSpace(label))?.Trim();
    }
}
