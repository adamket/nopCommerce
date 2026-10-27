using System.Text.Json;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Categories;

/// <summary>
/// One category template value. <see cref="AttributeCode"/> is the template
/// attribute code without Akeneo's "|uuid" suffix.
/// </summary>
public sealed record AkeneoCategoryValue(
    string AttributeCode,
    string Type,
    string Locale,
    string Channel,
    JsonElement Data)
{
    public bool IsImage =>
        string.Equals(Type, "image", StringComparison.OrdinalIgnoreCase) ||
        (Data.ValueKind == JsonValueKind.Object && Data.TryGetProperty("file_path", out _));

    /// <summary>The text of a text, textarea or rich text value; null for anything else.</summary>
    public string Text => Data.ValueKind == JsonValueKind.String ? Data.GetString() : null;

    public string ImageFilePath => ImageProperty("file_path");

    public string ImageFileName => ImageProperty("original_filename");

    public string ImageMimeType => ImageProperty("mime_type");

    public bool Matches(string locale, string channel) =>
        string.Equals(Normalize(Locale), Normalize(locale), StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Normalize(Channel), Normalize(channel), StringComparison.OrdinalIgnoreCase);

    private string ImageProperty(string name) =>
        Data.ValueKind == JsonValueKind.Object &&
        Data.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    internal static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// Reads the "values" of a category read with enriched attributes. Akeneo
/// returns an object keyed by "code|uuid|channel|locale"; the array form used
/// when writing is accepted as well.
/// </summary>
public static class AkeneoCategoryValueReader
{
    public static IReadOnlyList<AkeneoCategoryValue> Read(JsonElement values)
    {
        var result = new List<AkeneoCategoryValue>();

        if (values.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in values.EnumerateObject())
            {
                var value = ReadValue(property.Value, property.Name);
                if (value != null)
                    result.Add(value);
            }
        }
        else if (values.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in values.EnumerateArray())
            {
                var value = ReadValue(item, null);
                if (value != null)
                    result.Add(value);
            }
        }

        return result;
    }

    /// <summary>"description|96b88bf4-..." becomes "description".</summary>
    public static string BareAttributeCode(string attributeCode)
    {
        if (string.IsNullOrWhiteSpace(attributeCode))
            return null;

        var separator = attributeCode.IndexOf('|');
        var code = separator < 0 ? attributeCode : attributeCode[..separator];
        return string.IsNullOrWhiteSpace(code) ? null : code.Trim();
    }

    private static AkeneoCategoryValue ReadValue(JsonElement item, string key)
    {
        if (item.ValueKind != JsonValueKind.Object)
            return null;

        var code = BareAttributeCode(GetString(item, "attribute_code") ?? key);
        if (code == null)
            return null;

        var data = item.TryGetProperty("data", out var dataElement)
            ? dataElement.Clone()
            : default;

        return new AkeneoCategoryValue(
            code,
            GetString(item, "type"),
            AkeneoCategoryValue.Normalize(GetString(item, "locale")),
            AkeneoCategoryValue.Normalize(GetString(item, "channel")),
            data);
    }

    private static string GetString(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
