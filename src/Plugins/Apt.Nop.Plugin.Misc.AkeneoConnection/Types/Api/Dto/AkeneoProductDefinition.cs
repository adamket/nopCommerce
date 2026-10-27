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

    /// <summary>
    /// Association type code to its targets. Akeneo includes the associations a model or
    /// variant inherits from its ancestors.
    /// </summary>
    [JsonPropertyName("associations")]
    public Dictionary<string, AkeneoAssociationDefinition> Associations { get; set; } = new();

    /// <summary>
    /// Completeness per channel and locale, returned for products only when requested
    /// with with_completenesses=true. Product models have none.
    /// </summary>
    [JsonPropertyName("completenesses")]
    public List<AkeneoCompleteness> Completenesses { get; set; }
}

/// <summary>A product's completeness percentage for one channel and locale.</summary>
public class AkeneoCompleteness
{
    [JsonPropertyName("scope")]
    public string Scope { get; set; }

    [JsonPropertyName("locale")]
    public string Locale { get; set; }

    [JsonPropertyName("data")]
    public int? Data { get; set; }
}

/// <summary>
/// One association type's targets. Product models are listed by code. Products are listed by
/// identifier on the identifier-based endpoints and by UUID on the UUID endpoints.
/// </summary>
public class AkeneoAssociationDefinition
{
    [JsonPropertyName("products")]
    public List<string> Products { get; set; } = new();

    [JsonPropertyName("product_models")]
    public List<string> ProductModels { get; set; } = new();
}