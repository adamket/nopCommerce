using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.TestData;

internal static class AkeneoTestData
{
    public static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    public static AkeneoProductDefinition Product(
        string? identifier = "SKU-1",
        string? code = null,
        string? parent = null,
        string valuesJson = "{}",
        string? family = "family",
        string? familyVariant = "family_variant")
    {
        return new AkeneoProductDefinition
        {
            Uuid = "00000000-0000-0000-0000-000000000001",
            Identifier = identifier,
            Code = code,
            Parent = parent,
            Family = family,
            FamilyVariant = familyVariant,
            Enabled = true,
            Categories = new List<string>(),
            Values = Json(valuesJson)
        };
    }

    public static string ValueEntry(
        string dataJson,
        string? locale = null,
        string? scope = null,
        string? additionalJson = null)
    {
        var properties = new List<string>
        {
            $"\"locale\":{Serialize(locale)}",
            $"\"scope\":{Serialize(scope)}",
            $"\"data\":{dataJson}"
        };

        if (!string.IsNullOrWhiteSpace(additionalJson))
            properties.Add(additionalJson);

        return "{" + string.Join(",", properties) + "}";
    }

    private static string Serialize(string? value) =>
        value == null ? "null" : JsonSerializer.Serialize(value);
}
