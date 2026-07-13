using System.Text.Json.Serialization;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;

public class AkeneoAttributeOptionDefinition : LocalizableDefinition
{
    [JsonPropertyName("attribute")]
    public string Attribute { get; set; }

    [JsonPropertyName("sort_order")]
    public int SortOrder { get; set; }
}