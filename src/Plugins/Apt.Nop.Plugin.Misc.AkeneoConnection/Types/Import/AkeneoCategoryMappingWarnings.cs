namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;

internal sealed class AkeneoCategoryMappingWarnings
{
    private readonly SortedDictionary<string, int?> _categories = new(StringComparer.OrdinalIgnoreCase);

    public bool RemovalsSkipped { get; set; }

    public void Add(string code, int? missingDestinationId = null) =>
        _categories[code.Trim()] = missingDestinationId;

    public string BuildMessage()
    {
        if (_categories.Count == 0)
            return null;

        var categories = _categories.Select(pair => pair.Value.HasValue
            ? $"{pair.Key} (mapped nopCommerce category {pair.Value.Value} is missing or deleted)"
            : pair.Key);
        return $"Unresolved category mappings ({_categories.Count}): {string.Join(", ", categories)}. " +
            "These category assignments were skipped." +
            (RemovalsSkipped ? " Category removals were also skipped for products with unresolved categories." : string.Empty);
    }
}
