namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;

internal sealed class AkeneoMissingTokenWarnings
{
    private sealed record Group(string Header)
    {
        public SortedSet<int> ProductIds { get; } = new();
        public SortedSet<string> UnboundSources { get; } = new(StringComparer.Ordinal);
    }

    private readonly SortedDictionary<string, Group> _groups = new(StringComparer.Ordinal);

    public void Collect(AkeneoProductImportResult item, bool committed = true)
    {
        foreach (var missing in item.MissingTokenGroups)
        {
            var tokens = missing.Distinct(StringComparer.Ordinal).Select(token => $"{{{token}}}").ToArray();
            var key = string.Join(", ", tokens.OrderBy(token => token, StringComparer.Ordinal));
            if (!_groups.TryGetValue(key, out var group))
                _groups[key] = group = new Group(string.Join(", ", tokens));

            if (committed && item.NopProductId > 0)
                group.ProductIds.Add(item.NopProductId);
            else
                group.UnboundSources.Add(item.AkeneoIdentifier ?? item.Sku ?? item.AkeneoProductUuid ?? "(unknown source)");
        }
    }

    public IEnumerable<string> BuildMessages()
    {
        foreach (var group in _groups.Values)
        {
            var lines = new List<string> { $"Products missing: {group.Header}" };
            if (group.ProductIds.Count > 0)
                lines.Add(string.Join(", ", group.ProductIds));
            if (group.UnboundSources.Count > 0)
                lines.Add("No confirmed nopCommerce ID for Akeneo: " + string.Join(", ", group.UnboundSources));
            yield return string.Join(Environment.NewLine, lines);
        }
    }
}
