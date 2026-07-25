using System.Globalization;
using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

/// <summary>
/// Applies a deliberately small, deterministic transform schema. Keeping the
/// schema typed and centralized prevents each destination synchronizer from
/// interpreting TransformRuleJson differently.
/// </summary>
public class AkeneoValueTransformationService : IAkeneoValueTransformationService
{
    public AkeneoTransformationResult Transform(
        AkeneoResolvedProductValue source,
        AkeneoAttributeMapping mapping)
    {
        if (source == null || mapping == null ||
            string.IsNullOrWhiteSpace(mapping.TransformRuleJson))
        {
            return new AkeneoTransformationResult { Value = source };
        }

        try
        {
            using var document = JsonDocument.Parse(mapping.TransformRuleJson);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return Fail("Transform rule must be a JSON object.");
            }

            var values = source.DisplayValues is { Count: > 0 }
                ? source.DisplayValues.ToList()
                : string.IsNullOrWhiteSpace(source.DisplayValue)
                    ? new List<string>()
                    : new List<string> { source.DisplayValue };

            values = values.Select(value => ApplyStringRules(value, root)).ToList();

            if (root.TryGetProperty("distinct", out var distinctElement) &&
                distinctElement.ValueKind == JsonValueKind.True)
            {
                values = values
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            if (root.TryGetProperty("number", out var numberElement) &&
                numberElement.ValueKind == JsonValueKind.Object)
            {
                values = values
                    .Select(value => ApplyNumberRules(value, numberElement))
                    .ToList();
            }

            if (values.Count == 0 &&
                root.TryGetProperty("default", out var defaultElement) &&
                defaultElement.ValueKind == JsonValueKind.String)
            {
                values.Add(defaultElement.GetString());
            }

            var join = ", ";
            if (root.TryGetProperty("join", out var joinElement) &&
                joinElement.ValueKind == JsonValueKind.String)
            {
                join = joinElement.GetString() ?? string.Empty;
            }

            return new AkeneoTransformationResult
            {
                Value = new AkeneoResolvedProductValue
                {
                    AttributeCode = source.AttributeCode,
                    Locale = source.Locale,
                    Channel = source.Channel,
                    Currency = source.Currency,
                    SourceAttributeType = source.SourceAttributeType,
                    ReferenceDataName = source.ReferenceDataName,
                    RawData = source.RawData,
                    DisplayValues = values,
                    DisplayValue = values.Count switch
                    {
                        0 => string.Empty,
                        1 => values[0],
                        _ => string.Join(join, values)
                    }
                }
            };
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    private static string ApplyStringRules(string input, JsonElement root)
    {
        var value = input ?? string.Empty;

        if (root.TryGetProperty("trim", out var trimElement) &&
            trimElement.ValueKind == JsonValueKind.True)
        {
            value = value.Trim();
        }

        if (root.TryGetProperty("case", out var caseElement) &&
            caseElement.ValueKind == JsonValueKind.String)
        {
            value = caseElement.GetString()?.KeyPart() switch
            {
                "upper" => value.ToUpperInvariant(),
                "lower" => value.ToLowerInvariant(),
                _ => value
            };
        }

        if (root.TryGetProperty("replace", out var replaceElement))
        {
            if (replaceElement.ValueKind == JsonValueKind.Object)
            {
                value = ApplyReplacement(value, replaceElement);
            }
            else if (replaceElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var replacement in replaceElement.EnumerateArray()
                             .Where(item => item.ValueKind == JsonValueKind.Object))
                {
                    value = ApplyReplacement(value, replacement);
                }
            }
        }

        if (root.TryGetProperty("prefix", out var prefixElement) &&
            prefixElement.ValueKind == JsonValueKind.String)
        {
            value = (prefixElement.GetString() ?? string.Empty) + value;
        }

        if (root.TryGetProperty("suffix", out var suffixElement) &&
            suffixElement.ValueKind == JsonValueKind.String)
        {
            value += suffixElement.GetString() ?? string.Empty;
        }

        return value;
    }

    private static string ApplyReplacement(string value, JsonElement replacement)
    {
        if (!replacement.TryGetProperty("from", out var fromElement) ||
            fromElement.ValueKind != JsonValueKind.String)
        {
            return value;
        }

        var from = fromElement.GetString();
        var to = replacement.TryGetProperty("to", out var toElement) &&
                 toElement.ValueKind == JsonValueKind.String
            ? toElement.GetString()
            : string.Empty;

        return string.IsNullOrEmpty(from)
            ? value
            : value.Replace(from, to ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static string ApplyNumberRules(string input, JsonElement numberRules)
    {
        if (!decimal.TryParse(
                input,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var value))
        {
            return input;
        }

        if (numberRules.TryGetProperty("multiply", out var multiplyElement) &&
            multiplyElement.TryGetDecimal(out var multiplier))
        {
            value *= multiplier;
        }

        if (numberRules.TryGetProperty("add", out var addElement) &&
            addElement.TryGetDecimal(out var addend))
        {
            value += addend;
        }

        if (numberRules.TryGetProperty("round", out var roundElement) &&
            roundElement.TryGetInt32(out var decimals))
        {
            value = Math.Round(value, decimals, MidpointRounding.AwayFromZero);
        }

        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static AkeneoTransformationResult Fail(string error) => new()
    {
        Error = error
    };
}
