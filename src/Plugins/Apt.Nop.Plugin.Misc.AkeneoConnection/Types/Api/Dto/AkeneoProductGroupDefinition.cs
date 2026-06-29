using System.Text.Json.Serialization;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
public class AkeneoProductGroupDefinition : LocalizableDefinition
{
    [JsonPropertyName("type")]
    public string Type { get; set; }
}
