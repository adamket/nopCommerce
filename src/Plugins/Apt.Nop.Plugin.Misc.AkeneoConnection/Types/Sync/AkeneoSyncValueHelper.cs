using System.Text.Json;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;

public static class AkeneoSyncValueHelper
{
    public static IReadOnlyList<string> GetRawItems(
        AkeneoResolvedMappedValue mapped)
    {
        if (mapped?.Value?.RawData == null)
            return Array.Empty<string>();

        var data = mapped.Value.RawData.Value;

        if (data.ValueKind == JsonValueKind.Array)
        {
            return data.EnumerateArray()
                .Select(ConvertToString)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var singleValue = ConvertToString(data);

        return string.IsNullOrWhiteSpace(singleValue)
            ? Array.Empty<string>()
            : new[] { singleValue.Trim() };
    }



    public static IReadOnlyList<AkeneoResolvedOptionItem> GetOptionItems(
    AkeneoResolvedMappedValue mappedValue)
    {
        var value = mappedValue?.Value;
        if (value == null)
            return Array.Empty<AkeneoResolvedOptionItem>();

        // Codes and labels must be read in the same source order (no pre-distinct)
        // so index pairing is valid; dedupe only AFTER pairing.
        var codes = GetRawItemsInOrder(value);
        var labels = value.DisplayValues is { Count: > 0 }
            ? value.DisplayValues
            : (string.IsNullOrWhiteSpace(value.DisplayValue)
                ? Array.Empty<string>()
                : new[] { value.DisplayValue });

        var maxCount = Math.Max(codes.Count, labels.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<AkeneoResolvedOptionItem>();

        for (var i = 0; i < maxCount; i++)
        {
            var code = i < codes.Count ? codes[i]?.Trim() : null;
            var label = i < labels.Count ? labels[i]?.Trim() : null;
            var displayName = !string.IsNullOrWhiteSpace(label) ? label : code;

            if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(displayName))
                continue;

            if (!seen.Add(code ?? displayName))
                continue;

            items.Add(new AkeneoResolvedOptionItem
            {
                AkeneoOptionCode = code,
                DisplayName = displayName
            });
        }

        return items;
    }

    private static IReadOnlyList<string> GetRawItemsInOrder(
        AkeneoResolvedProductValue value)
    {
        if (value?.RawData == null)
            return Array.Empty<string>();

        var data = value.RawData.Value;

        if (data.ValueKind == JsonValueKind.Array)
        {
            return data.EnumerateArray()
                .Select(ConvertToString)
                .Select(v => v?.Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();
        }

        var single = ConvertToString(data)?.Trim();
        return string.IsNullOrWhiteSpace(single)
            ? Array.Empty<string>()
            : new[] { single };
    }

    private static string ConvertToString(
        JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }
}