using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public class AkeneoProductValueResolver : IAkeneoProductValueResolver
{
    public string GetValue(
        AkeneoProductDefinition product,
        string attributeCode,
        string locale = null,
        string channel = null,
        string currency = null)
    {
        return TryGetValue(product, attributeCode, out var resolvedValue, locale, channel, currency)
            ? resolvedValue.DisplayValue
            : string.Empty;
    }

    public bool TryGetValue(
        AkeneoProductDefinition product,
        string attributeCode,
        out AkeneoResolvedProductValue resolvedValue,
        string locale = null,
        string channel = null,
        string currency = null)
    {
        resolvedValue = null;

        if (product == null || string.IsNullOrWhiteSpace(attributeCode))
            return false;

        attributeCode = attributeCode.Trim();

        if (TryGetRootValue(product, attributeCode, out var rootValue))
        {
            var rootDisplay = FormatDataValue(rootValue, currency);

            resolvedValue = new AkeneoResolvedProductValue
            {
                AttributeCode = attributeCode,
                RawData = rootValue,
                DisplayValue = rootDisplay,
                DisplayValues = string.IsNullOrWhiteSpace(rootDisplay)
                    ? Array.Empty<string>()
                    : new[] { rootDisplay }
            };

            return true;
        }

        var valuesElement = product.Values;

        if (valuesElement.ValueKind != JsonValueKind.Object)
            return false;

        if (!valuesElement.TryGetProperty(attributeCode, out var attributeValuesElement) ||
            attributeValuesElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        if (!TrySelectBestValueObject(
                attributeValuesElement,
                locale,
                channel,
                out var selectedValueObject))
        {
            return false;
        }

        if (!selectedValueObject.TryGetProperty("data", out var dataElement))
            return false;

        var resolvedLocale = GetNullableStringProperty(selectedValueObject, "locale");
        var resolvedChannel = GetNullableStringProperty(selectedValueObject, "scope");
        var sourceAttributeType = GetNullableStringProperty(selectedValueObject, "attribute_type");
        var referenceDataName = GetNullableStringProperty(selectedValueObject, "reference_data_name");
        var displayValues = ResolveDisplayValues(selectedValueObject, dataElement, locale, currency);

        resolvedValue = new AkeneoResolvedProductValue
        {
            AttributeCode = attributeCode,
            Locale = resolvedLocale,
            Channel = resolvedChannel,
            Currency = currency,
            SourceAttributeType = sourceAttributeType,
            ReferenceDataName = referenceDataName,
            RawData = dataElement.Clone(),
            DisplayValue = displayValues.Count switch
            {
                0 => string.Empty,
                1 => displayValues[0],
                _ => string.Join(", ", displayValues)
            },
            DisplayValues = displayValues
        };

        return true;
    }

    private static bool TryGetRootValue(
        AkeneoProductDefinition product,
        string attributeCode,
        out JsonElement value)
    {
        value = default;

        if (product == null || string.IsNullOrWhiteSpace(attributeCode))
            return false;

        if (IsIdentifierAttribute(attributeCode))
            return TryCreateStringJsonValue(product.Identifier, out value);

        if (string.Equals(attributeCode, "uuid", StringComparison.OrdinalIgnoreCase))
            return TryCreateStringJsonValue(product.Uuid, out value);

        if (string.Equals(attributeCode, "identifier", StringComparison.OrdinalIgnoreCase))
            return TryCreateStringJsonValue(product.Identifier, out value);

        if (string.Equals(attributeCode, "code", StringComparison.OrdinalIgnoreCase))
            return TryCreateStringJsonValue(product.Code, out value);

        if (string.Equals(attributeCode, "family", StringComparison.OrdinalIgnoreCase))
            return TryCreateStringJsonValue(product.Family, out value);

        if (string.Equals(attributeCode, "family_variant", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(attributeCode, "familyVariant", StringComparison.OrdinalIgnoreCase))
        {
            return TryCreateStringJsonValue(product.FamilyVariant, out value);
        }

        if (string.Equals(attributeCode, "parent", StringComparison.OrdinalIgnoreCase))
            return TryCreateStringJsonValue(product.Parent, out value);

        if (string.Equals(attributeCode, "enabled", StringComparison.OrdinalIgnoreCase))
        {
            if (!product.Enabled.HasValue)
                return false;

            value = JsonSerializer.SerializeToElement(product.Enabled.Value);
            return true;
        }

        if (string.Equals(attributeCode, "categories", StringComparison.OrdinalIgnoreCase))
        {
            value = JsonSerializer.SerializeToElement(product.Categories ?? new List<string>());
            return true;
        }

        return false;
    }

    private static bool TryCreateStringJsonValue(
        string source,
        out JsonElement value)
    {
        value = default;

        if (string.IsNullOrWhiteSpace(source))
            return false;

        value = JsonSerializer.SerializeToElement(source);
        return true;
    }

    private static bool IsIdentifierAttribute(string attributeCode)
    {
        return string.Equals(attributeCode, "identifier", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(attributeCode, "sku", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TrySelectBestValueObject(
        JsonElement attributeValuesElement,
        string requestedLocale,
        string requestedChannel,
        out JsonElement selectedValueObject)
    {
        selectedValueObject = default;

        var candidates = attributeValuesElement
            .EnumerateArray()
            .Where(value => value.ValueKind == JsonValueKind.Object)
            .Select(value => new
            {
                Value = value,
                Score = ScoreValueObject(value, requestedLocale, requestedChannel)
            })
            .OrderByDescending(item => item.Score)
            .ToList();

        if (!candidates.Any())
            return false;

        var best = candidates.First();

        if (best.Score < 0)
            return false;

        selectedValueObject = best.Value;
        return true;
    }

    private static int ScoreValueObject(
        JsonElement valueObject,
        string requestedLocale,
        string requestedChannel)
    {
        var score = 0;

        var valueLocale = GetNullableStringProperty(valueObject, "locale");
        var valueChannel = GetNullableStringProperty(valueObject, "scope");

        score += ScoreLocaleValue(valueLocale, requestedLocale);
        score += ScoreContextValue(valueChannel, requestedChannel);

        return score;
    }

    private static int ScoreLocaleValue(
        string actualValue,
        string requestedValue)
    {
        if (!string.IsNullOrWhiteSpace(requestedValue))
        {
            if (LocaleEquals(actualValue, requestedValue))
                return 100;

            if (string.IsNullOrWhiteSpace(actualValue))
                return 10;

            return -1000;
        }

        return string.IsNullOrWhiteSpace(actualValue)
            ? 20
            : 1;
    }

    private static int ScoreContextValue(
        string actualValue,
        string requestedValue)
    {
        if (!string.IsNullOrWhiteSpace(requestedValue))
        {
            if (string.Equals(actualValue, requestedValue, StringComparison.OrdinalIgnoreCase))
                return 100;

            if (string.IsNullOrWhiteSpace(actualValue))
                return 10;

            return -1000;
        }

        return string.IsNullOrWhiteSpace(actualValue)
            ? 20
            : 1;
    }

    private static string FormatValueObject(
        JsonElement valueObject,
        JsonElement dataElement,
        string locale,
        string currency)
    {
        var linkedDataLabel = TryGetLinkedDataLabel(valueObject, locale);

        if (!string.IsNullOrWhiteSpace(linkedDataLabel))
            return linkedDataLabel;

        return FormatDataValue(dataElement, currency);
    }

    private static string FormatDataValue(
        JsonElement dataElement,
        string currency)
    {
        return dataElement.ValueKind switch
        {
            JsonValueKind.Null => string.Empty,
            JsonValueKind.Undefined => string.Empty,
            JsonValueKind.String => dataElement.GetString() ?? string.Empty,
            JsonValueKind.Number => dataElement.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Array => FormatArrayValue(dataElement, currency),
            JsonValueKind.Object => FormatObjectValue(dataElement, currency),
            _ => dataElement.GetRawText()
        };
    }

    private static string FormatArrayValue(
        JsonElement arrayElement,
        string currency)
    {
        var items = arrayElement.EnumerateArray().ToList();

        if (!items.Any())
            return string.Empty;

        if (LooksLikePriceCollection(items))
            return FormatPriceCollection(items, currency);

        return string.Join(", ", items
            .Select(item => FormatDataValue(item, currency))
            .Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static bool LooksLikePriceCollection(
        IList<JsonElement> items)
    {
        return items.Any(item =>
            item.ValueKind == JsonValueKind.Object &&
            item.TryGetProperty("amount", out _) &&
            item.TryGetProperty("currency", out _));
    }

    private static string FormatPriceCollection(
        IList<JsonElement> items,
        string currency)
    {
        JsonElement? selectedPrice = null;

        if (!string.IsNullOrWhiteSpace(currency))
        {
            selectedPrice = items.FirstOrDefault(item =>
                item.ValueKind == JsonValueKind.Object &&
                item.TryGetProperty("currency", out var currencyElement) &&
                string.Equals(currencyElement.GetString(), currency, StringComparison.OrdinalIgnoreCase));
        }

        selectedPrice ??= items.FirstOrDefault(item =>
            item.ValueKind == JsonValueKind.Object);

        if (!selectedPrice.HasValue ||
            selectedPrice.Value.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        var amount = selectedPrice.Value.TryGetProperty("amount", out var amountElement)
            ? amountElement.GetString()
            : null;

        return amount ?? string.Empty;
    }

    private static string FormatObjectValue(
        JsonElement objectElement,
        string currency)
    {
        if (objectElement.TryGetProperty("amount", out var amountElement) &&
            objectElement.TryGetProperty("unit", out var unitElement))
        {
            var amount = amountElement.ValueKind == JsonValueKind.String
                ? amountElement.GetString()
                : amountElement.GetRawText();

            var unit = unitElement.GetString();

            return string.IsNullOrWhiteSpace(unit)
                ? amount
                : $"{amount} {unit}";
        }

        if (objectElement.TryGetProperty("amount", out var priceAmountElement) &&
            objectElement.TryGetProperty("currency", out var priceCurrencyElement))
        {
            var amount = priceAmountElement.ValueKind == JsonValueKind.String
                ? priceAmountElement.GetString()
                : priceAmountElement.GetRawText();

            var objectCurrency = priceCurrencyElement.GetString();

            if (string.IsNullOrWhiteSpace(currency) ||
                string.Equals(currency, objectCurrency, StringComparison.OrdinalIgnoreCase))
            {
                return amount;
            }

            return string.Empty;
        }

        return objectElement.GetRawText();
    }

    private static string TryGetLinkedDataLabel(
        JsonElement valueObject,
        string locale)
    {
        if (!valueObject.TryGetProperty("linked_data", out var linkedData) ||
            linkedData.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (linkedData.TryGetProperty("labels", out var labels) &&
            labels.ValueKind == JsonValueKind.Object)
        {
            if (TryGetLocalizedLabel(labels, locale, out var localizedLabel))
                return localizedLabel;

            if (labels.TryGetProperty("en_US", out var englishLabel) &&
                englishLabel.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(englishLabel.GetString()))
            {
                return englishLabel.GetString();
            }

            foreach (var label in labels.EnumerateObject())
            {
                if (label.Value.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(label.Value.GetString()))
                {
                    return label.Value.GetString();
                }
            }
        }

        if (linkedData.TryGetProperty("label", out var labelElement) &&
            labelElement.ValueKind == JsonValueKind.String)
        {
            return labelElement.GetString();
        }

        return null;
    }

    private static bool TryGetLocalizedLabel(
        JsonElement labels,
        string requestedLocale,
        out string label)
    {
        label = null;

        if (labels.ValueKind != JsonValueKind.Object ||
            string.IsNullOrWhiteSpace(requestedLocale))
        {
            return false;
        }

        if (labels.TryGetProperty(requestedLocale, out var exact) &&
            exact.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(exact.GetString()))
        {
            label = exact.GetString();
            return true;
        }

        foreach (var candidate in labels.EnumerateObject())
        {
            if (!LocaleEquals(candidate.Name, requestedLocale) ||
                candidate.Value.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(candidate.Value.GetString()))
            {
                continue;
            }

            label = candidate.Value.GetString();
            return true;
        }

        return false;
    }

    private static bool LocaleEquals(string left, string right)
    {
        return string.Equals(
            left.TrimAndNormalizeText(),
            right.TrimAndNormalizeText(),
            StringComparison.OrdinalIgnoreCase);
    }


    private static string GetNullableStringProperty(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return null;

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static IReadOnlyList<string> ResolveDisplayValues(
        JsonElement valueObject,
        JsonElement dataElement,
        string locale,
        string currency)
    {
        var hasLinkedData =
            valueObject.TryGetProperty("linked_data", out var linkedData) &&
            linkedData.ValueKind == JsonValueKind.Object;

        if (dataElement.ValueKind == JsonValueKind.Array)
        {
            var items = new List<string>();

            foreach (var element in dataElement.EnumerateArray())
            {
                string display = null;

                if (element.ValueKind == JsonValueKind.String)
                {
                    var code = element.GetString();

                    if (hasLinkedData && !string.IsNullOrWhiteSpace(code))
                        display = GetOptionLabel(linkedData, code, locale);

                    display ??= code;
                }
                else
                {
                    display = FormatDataValue(element, currency);
                }

                if (!string.IsNullOrWhiteSpace(display))
                    items.Add(display.Trim());
            }

            return items;
        }

        if (dataElement.ValueKind == JsonValueKind.String && hasLinkedData)
        {
            var code = dataElement.GetString();
            var label = string.IsNullOrWhiteSpace(code)
                ? null
                : GetOptionLabel(linkedData, code, locale);

            if (!string.IsNullOrWhiteSpace(label))
                return new[] { label.Trim() };
        }

        var single = FormatValueObject(valueObject, dataElement, locale, currency);

        return string.IsNullOrWhiteSpace(single)
            ? Array.Empty<string>()
            : new[] { single.Trim() };
    }

    private static string GetOptionLabel(
        JsonElement linkedData,
        string code,
        string locale)
    {
        if (!linkedData.TryGetProperty(code, out var entry) ||
            entry.ValueKind != JsonValueKind.Object ||
            !entry.TryGetProperty("labels", out var labels) ||
            labels.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (TryGetLocalizedLabel(labels, locale, out var localized))
            return localized;

        if (labels.TryGetProperty("en_US", out var english) &&
            english.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(english.GetString()))
        {
            return english.GetString();
        }

        foreach (var label in labels.EnumerateObject())
        {
            if (label.Value.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(label.Value.GetString()))
            {
                return label.Value.GetString();
            }
        }

        return null;
    }
}