using System.Text.Json;
using System.Text.Json.Serialization;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;

public class AkeneoCategoryDefinition : LocalizableDefinition
{
    [JsonPropertyName("parent")]
    public string Parent { get; set; } = string.Empty;

    [JsonPropertyName("updated")]
    public string Updated { get; set; }

    /// <summary>
    /// Category template values, present only when the categories were read
    /// with enriched attributes. Read them with
    /// <see cref="Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Categories.AkeneoCategoryValueReader"/>.
    /// </summary>
    [JsonPropertyName("values")]
    public JsonElement Values { get; set; }

    public string GetDisplayNameWithParent(
        string locale = "en_US",
        bool includeCode = true,
        bool includeParent = true)
    {
        var displayName = GetDisplayName(locale, includeCode);
        var parent = Parent?.Trim();

        if (!includeParent || string.IsNullOrWhiteSpace(parent))
            return displayName;

        return $"{displayName} - parent: {parent}";
    }
}