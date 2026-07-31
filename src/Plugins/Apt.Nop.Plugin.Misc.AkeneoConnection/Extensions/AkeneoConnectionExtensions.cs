using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Extensions;

public static class AkeneoConnectionExtensions
{
    public static List<string> SplitCsv(this string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new List<string>();

        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string BuildCsv(
        this IEnumerable<string> values)
    {
        if (values == null)
            return null;

        var items = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return items.Any()
            ? string.Join(",", items)
            : null;
    }


    public static string GetRootString(this JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return null;

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }



    public static bool HasRequiredConnectionData(
        this AkeneoConnectionSettings settings)
    {
        if (settings == null)
            return false;

        return
            !string.IsNullOrWhiteSpace(settings.AkeneoConnectionBaseUrl) &&
            !string.IsNullOrWhiteSpace(settings.AkeneoConnectionClientId) &&
            !string.IsNullOrWhiteSpace(settings.AkeneoConnectionClientSecret) &&
            !string.IsNullOrWhiteSpace(settings.AkeneoConnectionUsername) &&
            !string.IsNullOrWhiteSpace(settings.AkeneoConnectionPassword);
    }


}