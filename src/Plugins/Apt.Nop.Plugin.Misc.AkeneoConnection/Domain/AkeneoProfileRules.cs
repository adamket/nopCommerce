using System.Text.Json;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;

public sealed class AkeneoProfileSkipRule
{
    public string AttributeCode { get; set; }
    public string Value { get; set; }
}

/// <summary>Validated profile configuration shared by the editor and execution paths.</summary>
public static class AkeneoProfileRules
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static IReadOnlyList<AkeneoProfileSkipRule> ParseSkipRules(string json)
    {
        var rows = Parse<AkeneoProfileSkipRule>(json);
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (row == null || string.IsNullOrWhiteSpace(row.AttributeCode) || string.IsNullOrWhiteSpace(row.Value))
                throw new ArgumentException("Every skip rule requires an attribute and a value.");
            row.AttributeCode = row.AttributeCode.Trim();
            row.Value = row.Value.Trim();
            if (!keys.Add(row.AttributeCode + "\u001f" + row.Value))
                throw new ArgumentException($"Duplicate skip rule for '{row.AttributeCode}' = '{row.Value}'.");
        }
        return rows;
    }

    private static List<T> Parse<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<T>();
        try
        {
            return JsonSerializer.Deserialize<List<T>>(json, JsonOptions)
                ?? throw new ArgumentException("Rules must be a JSON array.");
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("Rules must be a JSON array with valid rule fields.", ex);
        }
    }
}
