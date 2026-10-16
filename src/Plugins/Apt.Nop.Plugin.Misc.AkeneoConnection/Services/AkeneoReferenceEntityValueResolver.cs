using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Nop.Core.Caching;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

/// <summary>
/// Converts a product's reference-entity record code(s) into the configured
/// field value from each linked reference entity record.
/// </summary>
public class AkeneoReferenceEntityValueResolver(
    IAkeneoProductValueResolver productValueResolver,
    IAkeneoApiClient akeneoApiClient)
    : IAkeneoReferenceEntityValueResolver
{
    public const string RecordCodeField = "@code";

    // Cached for this resolver's lifetime only. It is scoped, so each sync run (or
    // request) reads records fresh: an edited record is never served stale for the
    // 60-minute static-cache lifetime, yet each record is fetched once per run.
    private readonly Dictionary<string, AkeneoReferenceEntityRecordDefinition> _records =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<AkeneoReferenceEntityAttributeDefinition>> _attributes =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<AkeneoReferenceEntityAttributeOptionDefinition>> _options =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task<AkeneoResolvedProductValue> ResolveAsync(
        AkeneoProductDefinition product,
        AkeneoAttributeMapping mapping,
        string locale = null,
        string channel = null,
        string currency = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(mapping);

        if (!productValueResolver.TryGetValue(
                product,
                mapping.AkeneoAttributeCode,
                out var linkedValue,
                locale,
                channel,
                currency))
        {
            return null;
        }

        var selectedField = mapping.AkeneoReferenceEntityAttributeCode?.Trim();

        // Backward compatibility for mappings created before reference-entity
        // field selection was introduced: continue returning the record code.
        if (string.IsNullOrWhiteSpace(selectedField))
            return linkedValue;

        var recordCodes = ExtractRecordCodes(linkedValue)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (recordCodes.Count == 0)
            return null;

        if (string.Equals(
                selectedField,
                RecordCodeField,
                StringComparison.OrdinalIgnoreCase))
        {
            return CreateResolvedValue(
                mapping.AkeneoAttributeCode,
                recordCodes,
                recordCodes,
                locale,
                channel,
                currency,
                linkedValue.ReferenceDataName,
                linkedValue.SourceAttributeType);
        }

        var referenceEntityCode = FirstNonEmpty(
            mapping.AkeneoReferenceEntityCode,
            linkedValue.ReferenceDataName);

        if (string.IsNullOrWhiteSpace(referenceEntityCode))
            return null;

        var resolvedItems = new List<ResolvedReferenceEntityFieldValue>();

        // Null unless the field is a single_option / multiple_options attribute.
        var optionLabels = await GetOptionLabelsAsync(
            referenceEntityCode,
            selectedField,
            locale,
            cancellationToken);

        foreach (var recordCode in recordCodes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var record = await GetRecordAsync(
                referenceEntityCode,
                recordCode,
                cancellationToken);

            if (record == null ||
                !TryResolveRecordField(
                    record,
                    selectedField,
                    locale,
                    channel,
                    currency,
                    out var fieldValues))
            {
                continue;
            }

            if (optionLabels != null)
            {
                // The record stores option codes. Each value is the option's label,
                // keyed by its code so the key is stable however the record orders
                // its options. An option with no label in any locale is left out:
                // its code is never presented as a value.
                foreach (var optionCode in fieldValues
                             .Where(value => !string.IsNullOrWhiteSpace(value))
                             .Select(value => value.Trim())
                             .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (optionLabels.TryGetValue(optionCode, out var label))
                        resolvedItems.Add(new ResolvedReferenceEntityFieldValue(optionCode, label));
                }

                continue;
            }

            var normalizedValues = fieldValues
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (var index = 0; index < normalizedValues.Count; index++)
            {
                // A nopCommerce specification option needs a stable source key.
                // Use the linked record code for scalar fields. If the chosen
                // reference field itself contains several values, append an index
                // while keeping the values in source order.
                var sourceKey = normalizedValues.Count == 1
                    ? recordCode
                    : $"{recordCode}:{index}";

                resolvedItems.Add(new ResolvedReferenceEntityFieldValue(
                    sourceKey,
                    normalizedValues[index]));
            }
        }

        resolvedItems = resolvedItems
            .GroupBy(item => item.SourceKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        if (resolvedItems.Count == 0)
            return null;

        return CreateResolvedValue(
            mapping.AkeneoAttributeCode,
            resolvedItems.Select(item => item.SourceKey).ToList(),
            resolvedItems.Select(item => item.DisplayValue).ToList(),
            locale,
            channel,
            currency,
            referenceEntityCode,
            linkedValue.SourceAttributeType);
    }

    /// <summary>
    /// Option code to label for a reference-entity option attribute: the requested locale,
    /// then en_US, then any locale. Options with no label are omitted. Null when the field
    /// is not an option attribute (or the record code pseudo-field).
    /// </summary>
    private async Task<IReadOnlyDictionary<string, string>> GetOptionLabelsAsync(
        string referenceEntityCode,
        string fieldCode,
        string locale,
        CancellationToken cancellationToken)
    {
        if (!_attributes.TryGetValue(referenceEntityCode, out var attributes))
        {
            attributes = await akeneoApiClient.GetReferenceEntityAttributesAsync(
                referenceEntityCode,
                cancellationToken);
            _attributes[referenceEntityCode] = attributes;
        }

        var attribute = attributes?.FirstOrDefault(item =>
            string.Equals(item.Code?.Trim(), fieldCode, StringComparison.OrdinalIgnoreCase));

        if (attribute?.Type is not ("single_option" or "multiple_options"))
            return null;

        var optionsKey = $"{referenceEntityCode}|{fieldCode}";
        if (!_options.TryGetValue(optionsKey, out var options))
        {
            options = await akeneoApiClient.GetReferenceEntityAttributeOptionsAsync(
                referenceEntityCode,
                attribute.Code,
                cancellationToken);
            _options[optionsKey] = options;
        }

        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var option in options ?? [])
        {
            var code = option.Code?.Trim();
            var label = GetLabelOnly(option, locale);
            if (!string.IsNullOrWhiteSpace(code) && label != null)
                labels.TryAdd(code, label);
        }

        return labels;
    }

    private static string GetLabelOnly(LocalizableDefinition option, string locale)
    {
        if (option.Labels == null || option.Labels.Count == 0)
            return null;

        foreach (var key in new[] { locale?.Trim(), "en_US" })
        {
            if (!string.IsNullOrWhiteSpace(key) &&
                option.Labels.TryGetValue(key, out var label) &&
                !string.IsNullOrWhiteSpace(label))
            {
                return label.Trim();
            }
        }

        return option.Labels.Values
            .FirstOrDefault(label => !string.IsNullOrWhiteSpace(label))
            ?.Trim();
    }

    private async Task<AkeneoReferenceEntityRecordDefinition> GetRecordAsync(
        string referenceEntityCode,
        string recordCode,
        CancellationToken cancellationToken)
    {
        var key = $"{referenceEntityCode}|{recordCode}";
        if (!_records.TryGetValue(key, out var record))
        {
            record = await akeneoApiClient.GetReferenceEntityRecordAsync(
                referenceEntityCode,
                recordCode,
                cancellationToken);
            _records[key] = record;
        }

        return record;
    }

    private static bool TryResolveRecordField(
        AkeneoReferenceEntityRecordDefinition record,
        string fieldCode,
        string locale,
        string channel,
        string currency,
        out IReadOnlyList<string> displayValues)
    {
        displayValues = Array.Empty<string>();

        if (record.Values.ValueKind != JsonValueKind.Object ||
            !record.Values.TryGetProperty(fieldCode, out var valuesElement) ||
            valuesElement.ValueKind != JsonValueKind.Array ||
            !TrySelectBestValueObject(valuesElement, locale, channel, out var valueObject) ||
            !valueObject.TryGetProperty("data", out var dataElement))
        {
            return false;
        }

        displayValues = FormatDataValues(dataElement, currency);
        return displayValues.Count > 0;
    }

    private static bool TrySelectBestValueObject(
        JsonElement valuesElement,
        string requestedLocale,
        string requestedChannel,
        out JsonElement selected)
    {
        selected = default;

        var best = valuesElement
            .EnumerateArray()
            .Where(value => value.ValueKind == JsonValueKind.Object)
            .Select(value => new
            {
                Value = value,
                Score = ScoreContext(
                    GetNullableString(value, "locale"),
                    requestedLocale) +
                    ScoreContext(
                        GetNullableString(value, "channel") ??
                        GetNullableString(value, "scope"),
                        requestedChannel)
            })
            .OrderByDescending(candidate => candidate.Score)
            .FirstOrDefault();

        if (best == null || best.Score < 0)
            return false;

        selected = best.Value;
        return true;
    }

    private static int ScoreContext(string actual, string requested)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            if (string.Equals(
                    actual.TrimAndNormalizeText(),
                    requested.TrimAndNormalizeText(),
                    StringComparison.OrdinalIgnoreCase))
            {
                return 100;
            }

            return string.IsNullOrWhiteSpace(actual) ? 10 : -1000;
        }

        return string.IsNullOrWhiteSpace(actual) ? 20 : 1;
    }

    private static IReadOnlyList<string> FormatDataValues(
        JsonElement data,
        string currency)
    {
        if (data.ValueKind == JsonValueKind.Array)
        {
            return data.EnumerateArray()
                .SelectMany(item => FormatDataValues(item, currency))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();
        }

        var formatted = FormatScalarValue(data, currency);

        return string.IsNullOrWhiteSpace(formatted)
            ? Array.Empty<string>()
            : new[] { formatted };
    }

    private static string FormatScalarValue(JsonElement data, string currency)
    {
        return data.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            JsonValueKind.String => data.GetString() ?? string.Empty,
            JsonValueKind.Number => data.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Object => FormatObjectValue(data, currency),
            _ => data.GetRawText()
        };
    }

    private static string FormatObjectValue(JsonElement data, string currency)
    {
        if (data.TryGetProperty("amount", out var amount) &&
            data.TryGetProperty("unit", out var unit))
        {
            var amountValue = amount.ValueKind == JsonValueKind.String
                ? amount.GetString()
                : amount.GetRawText();

            var unitValue = unit.ValueKind == JsonValueKind.String
                ? unit.GetString()
                : null;

            return string.IsNullOrWhiteSpace(unitValue)
                ? amountValue ?? string.Empty
                : $"{amountValue} {unitValue}";
        }

        if (data.TryGetProperty("amount", out var priceAmount) &&
            data.TryGetProperty("currency", out var priceCurrency))
        {
            var objectCurrency = priceCurrency.GetString();

            if (!string.IsNullOrWhiteSpace(currency) &&
                !string.Equals(currency, objectCurrency, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return priceAmount.ValueKind == JsonValueKind.String
                ? priceAmount.GetString() ?? string.Empty
                : priceAmount.GetRawText();
        }

        return data.GetRawText();
    }

    private static IEnumerable<string> ExtractRecordCodes(
        AkeneoResolvedProductValue linkedValue)
    {
        if (linkedValue.RawData is { } rawData)
        {
            if (rawData.ValueKind == JsonValueKind.String)
            {
                var value = rawData.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                    yield return value.Trim();
                yield break;
            }

            if (rawData.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in rawData.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String)
                        continue;

                    var value = item.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        yield return value.Trim();
                }

                yield break;
            }
        }

        if (linkedValue.DisplayValues != null)
        {
            foreach (var value in linkedValue.DisplayValues)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    yield return value.Trim();
            }
        }
    }

    private static AkeneoResolvedProductValue CreateResolvedValue(
        string attributeCode,
        IReadOnlyList<string> rawValues,
        IReadOnlyList<string> displayValues,
        string locale,
        string channel,
        string currency,
        string referenceEntityCode,
        string sourceAttributeType)
    {
        return new AkeneoResolvedProductValue
        {
            AttributeCode = attributeCode,
            Locale = locale,
            Channel = channel,
            Currency = currency,
            SourceAttributeType = sourceAttributeType,
            ReferenceDataName = referenceEntityCode,
            RawData = JsonSerializer.SerializeToElement(rawValues),
            DisplayValues = displayValues,
            DisplayValue = displayValues.Count switch
            {
                0 => string.Empty,
                1 => displayValues[0],
                _ => string.Join(", ", displayValues)
            }
        };
    }

    private static string GetNullableString(
        JsonElement element,
        string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
    }


    private sealed record ResolvedReferenceEntityFieldValue(
        string SourceKey,
        string DisplayValue);
}
