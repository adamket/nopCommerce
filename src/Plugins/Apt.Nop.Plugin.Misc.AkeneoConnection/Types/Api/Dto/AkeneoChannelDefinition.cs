namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using System.Text.Json.Serialization;

public class AkeneoChannelDefinition : LocalizableDefinition
{

    [JsonPropertyName("currencies")]
    public List<string> Currencies { get; set; } = new();

    [JsonPropertyName("locales")]
    public List<string> Locales { get; set; } = new();

}