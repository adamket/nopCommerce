using System.Text.Json.Serialization;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;

public class AkeneoCategoryDefinition : LocalizableDefinition
{
    [JsonPropertyName("parent")]
    public string Parent { get; set; } = string.Empty;

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