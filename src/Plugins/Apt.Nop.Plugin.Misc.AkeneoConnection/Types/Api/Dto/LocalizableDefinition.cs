using System.Text.Json.Serialization;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
public abstract class LocalizableDefinition
{
    [JsonPropertyName("labels")]
    public Dictionary<string, string> Labels { get; set; } = new();

    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    public string GetLabel(string locale = "en_US")
    {
        var normalizedLocale = string.IsNullOrWhiteSpace(locale)
            ? "en_US"
            : locale.Trim();

        if (Labels != null &&
            Labels.TryGetValue(normalizedLocale, out var label) &&
            !string.IsNullOrWhiteSpace(label))
        {
            return label.Trim();
        }

        if (Labels != null &&
            Labels.TryGetValue("en_US", out var englishLabel) &&
            !string.IsNullOrWhiteSpace(englishLabel))
        {
            return englishLabel.Trim();
        }

        return Code?.Trim() ?? string.Empty;
    }

    public string GetDisplayName(string locale = "en_US", bool includeCode = true)
    {
        var code = Code?.Trim();
        var label = GetLabel(locale)?.Trim();

        if (string.IsNullOrWhiteSpace(code))
            return label ?? string.Empty;

        if (string.IsNullOrWhiteSpace(label))
            return code;

        if (!includeCode)
            return label;

        if (string.Equals(code, label, StringComparison.OrdinalIgnoreCase))
            return code;

        return $"{label} ({code})";
    }

}
