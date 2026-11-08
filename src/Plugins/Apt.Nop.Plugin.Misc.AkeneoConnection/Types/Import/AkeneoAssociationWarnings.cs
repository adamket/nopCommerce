using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;

internal sealed class AkeneoAssociationWarnings
{
    private readonly Dictionary<(string Area, AkeneoEntityType Type, string Source), string[]> _warnings = new();

    // A successful retry replaces the initial findings, including clearing them all.
    public void Replace(string area, AkeneoEntityType type, string source, IEnumerable<string> warnings)
    {
        var key = (area, type, source);
        var distinct = warnings.Distinct(StringComparer.Ordinal).ToArray();
        if (distinct.Length == 0)
            _warnings.Remove(key);
        else
            _warnings[key] = distinct;
    }

    public void AddRetryFailure(string area, AkeneoEntityType type, string source, string reason)
    {
        _warnings.TryGetValue((area, type, source), out var previous);
        Replace(area, type, source, (previous ?? Array.Empty<string>()).Append(reason));
    }

    public IEnumerable<string> BuildMessages()
    {
        foreach (var area in _warnings.GroupBy(entry => entry.Key.Area).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var lines = area.SelectMany(entry => entry.Value.Select(warning => (Warning: warning, entry.Key.Source)))
                .GroupBy(entry => entry.Warning)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => $"{group.Key} (Sources: {string.Join(", ", group.Select(entry => entry.Source).Distinct().OrderBy(code => code, StringComparer.Ordinal))})");
            yield return area.Key + ":" + Environment.NewLine + string.Join(Environment.NewLine, lines);
        }
    }
}
