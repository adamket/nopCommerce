using System.Text.Json.Serialization;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;

public class AkeneoReferenceEntityAttributeDefinition : LocalizableDefinition
{
    [JsonPropertyName("type")]
    public string Type { get; set; }

    [JsonPropertyName("value_per_locale")]
    public bool ValuePerLocale { get; set; }

    [JsonPropertyName("value_per_channel")]
    public bool ValuePerChannel { get; set; }

    /// <summary>
    /// Populated when this reference-entity field links to another reference
    /// entity.
    /// </summary>
    [JsonPropertyName("reference_entity_code")]
    public string ReferenceEntityCode { get; set; }
}
