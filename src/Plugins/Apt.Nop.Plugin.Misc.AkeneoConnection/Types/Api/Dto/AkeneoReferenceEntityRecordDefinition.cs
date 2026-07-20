using System.Text.Json;
using System.Text.Json.Serialization;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;

public class AkeneoReferenceEntityRecordDefinition
{
    [JsonPropertyName("code")]
    public string Code { get; set; }

    [JsonPropertyName("values")]
    public JsonElement Values { get; set; }
}
