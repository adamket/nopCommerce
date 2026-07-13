using System.Text.Json.Serialization;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
public class AkeneoFamilyDefinition : LocalizableDefinition
{
    [JsonPropertyName("attributes")]
    public List<string> Attributes { get; set; } = [];
}
