using System.Text.Json.Serialization;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;

public class AkeneoAttributeDefinition : LocalizableDefinition
{

    [JsonPropertyName("type")]
    public string Type { get; set; }

    /// <summary>
    /// Reference entity code for akeneo_reference_entity and
    /// akeneo_reference_entity_collection attributes.
    /// </summary>
    [JsonPropertyName("reference_data_name")]
    public string ReferenceDataName { get; set; }

    [JsonPropertyName("group")]
    public string Group { get; set; }

    [JsonPropertyName("localizable")]
    public bool Localizable { get; set; }

    [JsonPropertyName("scopable")]
    public bool Scopable { get; set; }


    [JsonPropertyName("unique")]
    public bool Unique { get; set; }

    [JsonPropertyName("useable_as_grid_filter")]
    public bool UseableAsGridFilter { get; set; }
}