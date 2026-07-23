using System.Text.Json.Serialization;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;

public class AkeneoAssetAttributeDefinition : LocalizableDefinition
{
    [JsonPropertyName("type")]
    public string Type { get; set; }

    [JsonPropertyName("value_per_locale")]
    public bool ValuePerLocale { get; set; }

    [JsonPropertyName("value_per_channel")]
    public bool ValuePerChannel { get; set; }

    [JsonPropertyName("media_type")]
    public string MediaType { get; set; }
}
