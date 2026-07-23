using System.Text.Json;
using System.Text.Json.Serialization;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;

public class AkeneoAssetDefinition
{
    [JsonPropertyName("code")]
    public string Code { get; set; }

    [JsonPropertyName("asset_family_code")]
    public string AssetFamilyCode { get; set; }

    [JsonPropertyName("values")]
    public JsonElement Values { get; set; }

    [JsonPropertyName("created")]
    public DateTime? CreatedOnUtc { get; set; }

    [JsonPropertyName("updated")]
    public DateTime? UpdatedOnUtc { get; set; }
}
