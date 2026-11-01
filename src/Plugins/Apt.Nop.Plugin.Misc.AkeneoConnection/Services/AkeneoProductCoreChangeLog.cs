using System.Text.Encodings.Web;
using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services.Planning;
using Nop.Core.Domain.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public partial class AkeneoProductCoreSynchronizer
{
    // These are plain-text log values; the log view separately HTML-encodes them.
    private static readonly JsonSerializerOptions CoreLogValueJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static Dictionary<string, string> CaptureCoreFieldValues(Product product, AkeneoExecutableSectionPlan plan) =>
        plan.Operations
            .Where(operation => operation.Preview?.Area is "Product fields" or "Product lifecycle")
            .Select(operation => operation.Preview.Target)
            .Where(target => !string.IsNullOrWhiteSpace(target))
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(target => target, target => GetProductFieldValue(product, target), StringComparer.Ordinal);

    private static string BuildCoreChangesMessage(Product product, IReadOnlyDictionary<string, string> previousValues)
    {
        var changes = previousValues
            .Select(previous => new { Field = previous.Key, Before = previous.Value, After = GetProductFieldValue(product, previous.Key) })
            .Where(change => !string.Equals(change.Before, change.After, StringComparison.Ordinal))
            .Select(change => $"{change.Field}: {FormatCoreLogValue(change.Before)} → {FormatCoreLogValue(change.After)}");

        var details = string.Join("; ", changes);
        var summary = $"Updated core product fields for product ID {product.Id}.";
        return details.Length == 0 ? summary : summary + " " + details;
    }

    private static string FormatCoreLogValue(string value)
    {
        if (value == null)
            return "(null)";

        // Item logs have a shared 4,000-character budget, including other sections.
        // Escape quotes and line breaks so each value remains unambiguous.
        const int previewLength = 160;
        if (value.Length <= previewLength)
            return JsonSerializer.Serialize(value, CoreLogValueJsonOptions);

        return JsonSerializer.Serialize(value[..previewLength], CoreLogValueJsonOptions) + $"… (truncated; {value.Length} characters)";
    }
}
