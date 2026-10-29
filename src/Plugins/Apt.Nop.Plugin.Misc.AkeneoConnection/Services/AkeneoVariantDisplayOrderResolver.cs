using System.Globalization;
using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public interface IAkeneoVariantDisplayOrderResolver
{
    Task<AkeneoVariantDisplayOrder> ResolveAsync(
        AkeneoProductDefinition source,
        IReadOnlyList<AkeneoVariantDisplayOrderKey> keys,
        string locale = null,
        string channel = null,
        string currency = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads each sort key's position for one variant: a select's Akeneo option
/// order, a number, or a number field on the linked reference-entity record.
/// A missing value sorts last, with a warning.
/// </summary>
public class AkeneoVariantDisplayOrderResolver(
    IAkeneoProductValueResolver productValueResolver,
    IAkeneoReferenceEntityValueResolver referenceEntityValueResolver,
    IAkeneoApiClient akeneoApiClient)
    : IAkeneoVariantDisplayOrderResolver
{
    // Scoped, so a run reads each attribute's options once and never serves a stale order to the next run.
    private readonly Dictionary<string, IReadOnlyDictionary<string, int>> _optionOrders =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task<AkeneoVariantDisplayOrder> ResolveAsync(
        AkeneoProductDefinition source,
        IReadOnlyList<AkeneoVariantDisplayOrderKey> keys,
        string locale = null,
        string channel = null,
        string currency = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var positions = new List<int>();
        var byAttribute = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();

        foreach (var key in keys ?? Array.Empty<AkeneoVariantDisplayOrderKey>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (value, problem) = await ReadAsync(source, key, locale, channel, currency, cancellationToken);
            if (problem != null)
                warnings.Add($"Variant display order: {problem}, so it sorts last on '{key}'.");

            var position = AkeneoVariantDisplayOrderKeys.ToPosition(value);
            if (value > AkeneoVariantDisplayOrderKeys.MaximumPosition)
                warnings.Add($"Variant display order: '{key}' is {value}, above {AkeneoVariantDisplayOrderKeys.MaximumPosition}, so it sorts as {position}.");

            positions.Add(position);
            byAttribute[key.AttributeCode] = position;
        }

        return new AkeneoVariantDisplayOrder
        {
            Value = AkeneoVariantDisplayOrderKeys.Compose(positions),
            PositionsByAttributeCode = byAttribute,
            Warnings = warnings
        };
    }

    private async Task<(decimal? Value, string Problem)> ReadAsync(
        AkeneoProductDefinition source,
        AkeneoVariantDisplayOrderKey key,
        string locale,
        string channel,
        string currency,
        CancellationToken cancellationToken)
    {
        if (key.RecordFieldCode != null)
        {
            // Say which step came up empty: no link on the variant, a link that is not a
            // record, or a record without the field for this run's locale and channel.
            if (!productValueResolver.TryGetValue(source, key.AttributeCode, out var link, locale, channel, currency) ||
                link == null)
            {
                return (null, $"the variant has no '{key.AttributeCode}' value (check the product attribute code that links the record)");
            }

            var recordCode = FirstCode(link.RawData) ?? link.DisplayValue;
            if (string.IsNullOrWhiteSpace(link.ReferenceDataName) &&
                !(link.SourceAttributeType ?? string.Empty).Contains("reference", StringComparison.OrdinalIgnoreCase))
            {
                return (null, $"'{key.AttributeCode}' is not a reference-entity attribute, so it has no '{key.RecordFieldCode}' field");
            }

            var field = await referenceEntityValueResolver.ResolveAsync(
                source,
                new AkeneoAttributeMapping
                {
                    AkeneoAttributeCode = key.AttributeCode,
                    AkeneoReferenceEntityAttributeCode = key.RecordFieldCode
                },
                locale,
                channel,
                currency,
                cancellationToken);

            var text = field?.DisplayValues?.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item)) ?? field?.DisplayValue;
            if (string.IsNullOrWhiteSpace(text))
            {
                var scope = string.Join(", ", new[] { locale, channel }.Where(item => !string.IsNullOrWhiteSpace(item)));
                return (null, $"the '{link.ReferenceDataName ?? key.AttributeCode}' record '{recordCode}' linked by '{key.AttributeCode}' " +
                              $"has no '{key.RecordFieldCode}' value{(scope.Length > 0 ? $" for {scope}" : string.Empty)} " +
                              "(or the record could not be read)");
            }

            return TryParseNumber(text, out var number)
                ? (number, null)
                : (null, $"'{key}' is '{text}', not a number");
        }

        if (!productValueResolver.TryGetValue(source, key.AttributeCode, out var resolved, locale, channel, currency) ||
            resolved == null)
        {
            return (null, $"the variant has no '{key.AttributeCode}' value");
        }

        var type = resolved.SourceAttributeType ?? string.Empty;

        if (type.Contains("select", StringComparison.OrdinalIgnoreCase))
        {
            var optionCode = FirstCode(resolved.RawData);
            if (optionCode == null)
                return (null, $"the variant has no '{key.AttributeCode}' option");

            var orders = await GetOptionOrdersAsync(key.AttributeCode, cancellationToken);
            return orders.TryGetValue(optionCode, out var sortOrder)
                ? (sortOrder, null)
                : (null, $"Akeneo has no '{optionCode}' option on '{key.AttributeCode}'");
        }

        if (type.Contains("reference", StringComparison.OrdinalIgnoreCase))
            return (null, $"'{key.AttributeCode}' links a record; name the record field to sort by, e.g. '{key.AttributeCode}.display_order'");

        var amount = ReadAmount(resolved.RawData) ?? resolved.DisplayValue;
        return TryParseNumber(amount, out var parsed)
            ? (parsed, null)
            : (null, $"'{key.AttributeCode}' is '{amount}', not a number or a select");
    }

    private async Task<IReadOnlyDictionary<string, int>> GetOptionOrdersAsync(
        string attributeCode,
        CancellationToken cancellationToken)
    {
        if (_optionOrders.TryGetValue(attributeCode, out var cached))
            return cached;

        var options = await akeneoApiClient.GetAttributeOptionDefinitionsAsync(attributeCode, 100, cancellationToken);
        var orders = (options ?? Array.Empty<AkeneoAttributeOptionDefinition>())
            .Where(option => !string.IsNullOrWhiteSpace(option.Code))
            .GroupBy(option => option.Code.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().SortOrder, StringComparer.OrdinalIgnoreCase);

        _optionOrders[attributeCode] = orders;
        return orders;
    }

    private static string FirstCode(JsonElement? data) => data switch
    {
        { ValueKind: JsonValueKind.String } value => value.GetString()?.Trim(),
        { ValueKind: JsonValueKind.Array } value => value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()?.Trim())
            .FirstOrDefault(code => !string.IsNullOrEmpty(code)),
        _ => null
    };

    // Numbers come as a JSON number or string; measurements as { "amount": ..., "unit": ... }.
    private static string ReadAmount(JsonElement? data) => data switch
    {
        { ValueKind: JsonValueKind.Number } value => value.GetRawText(),
        { ValueKind: JsonValueKind.String } value => value.GetString(),
        { ValueKind: JsonValueKind.Object } value when value.TryGetProperty("amount", out var amount) =>
            amount.ValueKind == JsonValueKind.String ? amount.GetString() : amount.GetRawText(),
        _ => null
    };

    private static bool TryParseNumber(string text, out decimal number) =>
        decimal.TryParse(text?.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out number);
}
