namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using System.Text.Json.Serialization;

public class AkeneoChannelDefinition
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("currencies")]
    public List<string> Currencies { get; set; } = new();

    [JsonPropertyName("locales")]
    public List<string> Locales { get; set; } = new();

    [JsonPropertyName("labels")]
    public Dictionary<string, string> Labels { get; set; } = new();
}