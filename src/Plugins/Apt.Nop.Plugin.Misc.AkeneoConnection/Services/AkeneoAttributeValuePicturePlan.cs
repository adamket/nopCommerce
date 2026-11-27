using Nop.Core;
using Nop.Core.Domain.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

internal static class AkeneoAttributeValuePicturePlan
{
    public static ProductAttributeValue SelectValue(IEnumerable<ProductAttributeValue> values, string key)
    {
        var matches = values.Where(value => key == null || string.Equals(
            value.Name,
            key, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count != 1)
            throw new NopException($"Expected exactly one target attribute value{(key == null ? "" : $" with key '{key}'")}, but found {matches.Count}. Existing value pictures were preserved.");
        return matches[0];
    }

    public static IReadOnlyList<int> Reconcile(IList<int> current, IReadOnlyList<int> desired,
        ISet<int> owned, bool merge, bool replaceAll)
    {
        var retained = current.Where(id => merge || (!replaceAll && !owned.Contains(id))).ToList();
        // Preserve a manually maintained primary picture. Otherwise the first source image leads.
        var primary = current.FirstOrDefault();
        var manualPrimary = !replaceAll && primary > 0 && !owned.Contains(primary);
        return (manualPrimary ? new[] { primary } : Array.Empty<int>())
            .Concat(desired).Concat(retained).Where(id => id > 0).Distinct().ToList();
    }
}
