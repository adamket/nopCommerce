using System.Text.Json.Serialization;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;

public class AkeneoAssetFamilyDefinition : LocalizableDefinition
{
    [JsonPropertyName("attribute_as_main_media")]
    public string AttributeAsMainMedia { get; set; }
}
