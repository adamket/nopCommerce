using System.Text.Json;
using System.Text.Json.Serialization;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;

public class AkeneoProductDefinition
{
    [JsonPropertyName("uuid")]
    public string Uuid { get; set; }

    [JsonPropertyName("identifier")]
    public string Identifier { get; set; }        // legacy / products only

    [JsonPropertyName("code")]
    public string Code { get; set; }              // product models only

    [JsonPropertyName("family")]
    public string Family { get; set; }

    [JsonPropertyName("family_variant")]
    public string FamilyVariant { get; set; }

    [JsonPropertyName("parent")]
    public string Parent { get; set; }

    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    [JsonPropertyName("categories")]
    public List<string> Categories { get; set; } = new();

    // Deliberately left as raw JSON — dynamic, locale/scope-addressed, resolved via
    // attribute mappings + productValueResolver, not modeled as named properties.
    [JsonPropertyName("values")]
    public JsonElement Values { get; set; }
}