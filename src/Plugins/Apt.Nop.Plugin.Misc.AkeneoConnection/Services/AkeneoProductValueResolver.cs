using System.Globalization;
using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public class AkeneoProductValueResolver : IAkeneoProductValueResolver
{
    public string GetValue(
        JsonElement product,
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
        JsonElement product,
        string attributeCode,
        out AkeneoResolvedProductValue resolvedValue,
        string locale = null,
        string channel = null,
        string currency = null)
    {
        resolvedValue = null;

        if (string.IsNullOrWhiteSpace(attributeCode))
            return false;

        attributeCode = attributeCode.Trim();

        if (TryGetRootValue(product, attributeCode, out var rootValue))
        {
            resolvedValue = new AkeneoResolvedProductValue
            {
                AttributeCode = attributeCode,
                RawData = rootValue,
                DisplayValue = FormatDataValue(rootValue, currency)
            };

            return true;
        }

        if (!product.TryGetProperty("values", out var valuesElement) ||
            valuesElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

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

        resolvedValue = new AkeneoResolvedProductValue
        {
            AttributeCode = attributeCode,
            Locale = resolvedLocale,
            Channel = resolvedChannel,
            Currency = currency,
            RawData = dataElement.Clone(),
            DisplayValue = FormatValueObject(selectedValueObject, dataElement, locale, currency)
        };

        return true;
    }

    private static bool TryGetRootValue(
        JsonElement product,
        string attributeCode,
        out JsonElement value)
    {
        value = default;

        if (product.ValueKind != JsonValueKind.Object)
            return false;

        if (product.TryGetProperty(attributeCode, out value))
            return true;

        if (IsIdentifierAttribute(attributeCode) &&
            product.TryGetProperty("identifier", out value))
        {
            return true;
        }

        if (string.Equals(attributeCode, "uuid", StringComparison.OrdinalIgnoreCase) &&
            product.TryGetProperty("uuid", out value))
        {
            return true;
        }

        return false;
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

        if (best.Score < -500)
            best = candidates.First();

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

        score += ScoreContextValue(valueLocale, requestedLocale);
        score += ScoreContextValue(valueChannel, requestedChannel);

        return score;
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
            if (!string.IsNullOrWhiteSpace(locale) &&
                labels.TryGetProperty(locale, out var localizedLabel) &&
                localizedLabel.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(localizedLabel.GetString()))
            {
                return localizedLabel.GetString();
            }

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
}