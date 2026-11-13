using System.Diagnostics;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;

/// <summary>
/// The steps of one batch run: each step's progress goes to the sync page, and
/// how long each took goes into the run summary. A step with no work is not reported.
/// </summary>
internal sealed class AkeneoSyncSteps(Action<string, string, int, int> report)
{
    public const string Products = "Processing products";

    private readonly List<string> _timings = [];
    private string _current;
    private string _unit;
    private int _total;
    private readonly Stopwatch _watch = new();

    /// <summary>Reports <paramref name="done"/> of <paramref name="total"/> units of a step; a new step name starts its timer.</summary>
    public void Report(string step, string unit, int done, int total)
    {
        if (total <= 0)
            return;

        if (!string.Equals(step, _current, StringComparison.Ordinal))
        {
            Close();
            _current = step;
            _watch.Restart();
        }

        _unit = unit;
        _total = total;
        report?.Invoke(step, unit, done, total);
    }

    /// <summary>Ends timing; null when no step reported any work.</summary>
    public string BuildSummary()
    {
        Close();
        return _timings.Count == 0 ? null : "Step timings: " + string.Join("; ", _timings) + ".";
    }

    private void Close()
    {
        if (_current == null)
            return;

        _timings.Add($"{_current} {Format(_watch.Elapsed)} ({_total} {_unit})");
        _current = null;
    }

    internal static string Format(TimeSpan elapsed) =>
        elapsed.TotalMinutes >= 1
            ? $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds}s"
            : elapsed.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "s";
}
