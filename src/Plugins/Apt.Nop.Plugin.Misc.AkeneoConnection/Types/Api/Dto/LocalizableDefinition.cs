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
        if (Labels.TryGetValue(locale, out var label) && !string.IsNullOrWhiteSpace(label))
            return label;
        if (Labels.TryGetValue("en_US", out var englishLabel) && !string.IsNullOrWhiteSpace(englishLabel))
            return englishLabel;
        return Code;
    }

    //private static string BuildOptionText(
    //    string code,
    //    string label)
    //{
    //    if (string.IsNullOrWhiteSpace(label))
    //        return code;

    //    if (string.Equals(label, code, StringComparison.OrdinalIgnoreCase))
    //        return code;

    //    return $"{label} ({code})";
    //}
}
