namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types;

/// <summary>
/// One sort key of a family's variant display order: a product attribute,
/// plus the reference-entity record field to read when the attribute links a
/// record ("root_type.display_order").
/// </summary>
public sealed record AkeneoVariantDisplayOrderKey(string AttributeCode, string RecordFieldCode)
{
    public override string ToString() =>
        RecordFieldCode == null ? AttributeCode : $"{AttributeCode}.{RecordFieldCode}";
}

/// <summary>A variant's display order from its family's sort keys.</summary>
public sealed class AkeneoVariantDisplayOrder
{
    /// <summary>All keys combined, for the one value that represents the variant.</summary>
    public int Value { get; init; }

    /// <summary>Each key's own position by attribute code, for a value that stands for one axis.</summary>
    public IReadOnlyDictionary<string, int> PositionsByAttributeCode { get; init; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

/// <summary>
/// The sort keys that order a family's variant values in nopCommerce. Each key
/// becomes a 0-999 position: a select's Akeneo option order, a number, or a
/// number field on a linked reference-entity record. The positions are
/// combined into one display order, first key most significant, so each
/// variant's order is known without reading its siblings.
/// </summary>
public static class AkeneoVariantDisplayOrderKeys
{
    public const int MaximumKeys = 3;

    /// <summary>The largest position a key can take; a missing value sorts last with it.</summary>
    public const int MaximumPosition = 999;

    private const int SlotSize = MaximumPosition + 1;

    public static bool TryParse(string value, out IReadOnlyList<AkeneoVariantDisplayOrderKey> keys, out string error)
    {
        keys = Array.Empty<AkeneoVariantDisplayOrderKey>();
        error = null;

        var parts = (value ?? string.Empty)
            .Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length > MaximumKeys)
        {
            error = $"Use at most {MaximumKeys} sort keys.";
            return false;
        }

        var parsed = new List<AkeneoVariantDisplayOrderKey>();
        foreach (var part in parts)
        {
            var pieces = part.Split('.');
            if (pieces.Length > 2 || pieces.Any(piece => !IsCode(piece)))
            {
                error = $"'{part}' is not a sort key. Use an attribute code, or attribute.field for a reference-entity record field.";
                return false;
            }

            if (parsed.Any(key => string.Equals(key.AttributeCode, pieces[0], StringComparison.OrdinalIgnoreCase)))
            {
                error = $"'{pieces[0]}' is listed more than once.";
                return false;
            }

            parsed.Add(new AkeneoVariantDisplayOrderKey(pieces[0], pieces.Length == 2 ? pieces[1] : null));
        }

        keys = parsed;
        return true;
    }

    public static IReadOnlyList<AkeneoVariantDisplayOrderKey> Parse(string value) =>
        TryParse(value, out var keys, out _) ? keys : Array.Empty<AkeneoVariantDisplayOrderKey>();

    /// <summary>Keeps a position within 0-999; null (no value) sorts last.</summary>
    public static int ToPosition(decimal? value) => value switch
    {
        null => MaximumPosition,
        < 0 => 0,
        > MaximumPosition => MaximumPosition,
        _ => (int)Math.Round(value.Value, MidpointRounding.AwayFromZero)
    };

    /// <summary>
    /// Combines the positions, first most significant: (20, 2, 1) becomes
    /// 20,002,001. Three keys fit nopCommerce's int display order.
    /// </summary>
    public static int Compose(IReadOnlyList<int> positions)
    {
        var order = 0L;
        foreach (var position in positions)
            order = order * SlotSize + Math.Clamp(position, 0, MaximumPosition);

        return (int)Math.Min(order, int.MaxValue);
    }

    /// <summary>The gap between ranked display orders, so a value can be slotted in by hand.</summary>
    public const int RankStep = 10;

    /// <summary>
    /// Display orders 0, 10, 20… for one attribute's values, by ID: values with a
    /// sort key by key, then values without one; ties keep their current order.
    /// </summary>
    public static IReadOnlyDictionary<int, int> Rank(IEnumerable<(int Id, int? Key, int DisplayOrder)> values) =>
        values
            .OrderBy(value => value.Key.HasValue ? 0 : 1)
            .ThenBy(value => value.Key ?? 0)
            .ThenBy(value => value.DisplayOrder)
            .ThenBy(value => value.Id)
            .Select((value, index) => (value.Id, Order: index * RankStep))
            .ToDictionary(item => item.Id, item => item.Order);

    private static bool IsCode(string value) =>
        value.Length > 0 && value.All(character => char.IsLetterOrDigit(character) || character == '_');
}
