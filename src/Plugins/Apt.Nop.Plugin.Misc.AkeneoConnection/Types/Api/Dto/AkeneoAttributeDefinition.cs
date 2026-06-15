using System.Text.Json.Serialization;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;

public class AkeneoAttributeDefinition
{
    [JsonPropertyName("code")]
    public string Code { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; }

    [JsonPropertyName("group")]
    public string Group { get; set; }

    [JsonPropertyName("localizable")]
    public bool Localizable { get; set; }

    [JsonPropertyName("scopable")]
    public bool Scopable { get; set; }


    [JsonPropertyName("unique")]
    public bool Unique { get; set; }

    [JsonPropertyName("useable_as_grid_filter")]
    public bool UseableAsGridFilter { get; set; }

    [JsonPropertyName("labels")]
    public Dictionary<string, string> Labels { get; set; } = new();

    public string GetLabel(string locale = "en_US")
    {
        if (Labels.TryGetValue(locale, out var label) && !string.IsNullOrWhiteSpace(label))
            return label;

        if (Labels.TryGetValue("en_US", out var englishLabel) && !string.IsNullOrWhiteSpace(englishLabel))
            return englishLabel;

        return Code;
    }
}