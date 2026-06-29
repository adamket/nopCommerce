using System.Text.Json.Serialization;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
public class AkeneoFamilyVariantDefinition : LocalizableDefinition
{
    [JsonPropertyName("variant_attribute_sets")]
    public List<AkeneoVariantAttributeSet> VariantAttributeSets { get; set; } = new();
}

public class AkeneoVariantAttributeSet
{
    [JsonPropertyName("level")]
    public int Level { get; set; }

    [JsonPropertyName("axes")]
    public List<string> Axes { get; set; } = new();

    [JsonPropertyName("attributes")]
    public List<string> Attributes { get; set; } = new();
}

public class AkeneoFamilyAxis
{
    public string AttributeCode { get; set; } = string.Empty;
    public int Level { get; set; }
}