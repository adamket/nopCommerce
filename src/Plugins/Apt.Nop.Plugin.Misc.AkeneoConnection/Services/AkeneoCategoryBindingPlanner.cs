using System.Text.RegularExpressions;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Binding;
using Nop.Core.Domain.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

/// <summary>
/// The binding decision for one Akeneo category.
/// </summary>
public sealed record AkeneoCategoryBindingPlan(
    string AkeneoCode,
    AkeneoBindingStatus Status,
    int? NopCategoryId,
    string Message);

/// <summary>
/// Matches Akeneo categories to existing nopCommerce categories the way the
/// nopCommerce migration created them: an Akeneo category code is the
/// nopCommerce category name lowercased, with every run of characters other
/// than a-z and 0-9 turned into "_", and its parent is the parent category's
/// code, or the tree root for a top-level category. A match must agree on
/// both the code and the parent. Existing mappings are never changed, and
/// anything uncertain is reported for manual mapping instead of bound.
/// </summary>
public static class AkeneoCategoryBindingPlanner
{
    private static readonly Regex NonAlphanumeric = new("[^a-z0-9]+", RegexOptions.Compiled);

    /// <summary>
    /// The Akeneo code the migration gives a nopCommerce category name, or
    /// null when the name has no letters or digits.
    /// </summary>
    public static string CodeFor(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var code = NonAlphanumeric.Replace(name.ToLowerInvariant(), "_").Trim('_');
        return code.Length == 0 ? null : code;
    }

    /// <param name="akeneoCategories">Every Akeneo category, roots included.</param>
    /// <param name="nopCategories">nopCommerce categories; deleted ones are ignored.</param>
    /// <param name="existingMappings">Akeneo category code to mapped nopCommerce category ID.</param>
    public static IReadOnlyList<AkeneoCategoryBindingPlan> Plan(
        IReadOnlyCollection<AkeneoCategoryDefinition> akeneoCategories,
        IReadOnlyCollection<Category> nopCategories,
        IReadOnlyDictionary<string, int> existingMappings)
    {
        var akeneoByCode = akeneoCategories
            .Where(category => !string.IsNullOrWhiteSpace(category.Code))
            .GroupBy(category => category.Code.Trim(), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var liveNop = nopCategories
            .Where(category => !category.Deleted)
            .ToDictionary(category => category.Id);

        var nopByCode = liveNop.Values
            .Select(category => (Code: CodeFor(category.Name), Category: category))
            .Where(item => item.Code != null)
            .GroupBy(item => item.Code, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Category).ToList(),
                StringComparer.Ordinal);

        // nopCommerce categories some Akeneo category is already bound to.
        var boundTo = existingMappings
            .GroupBy(mapping => mapping.Value)
            .ToDictionary(group => group.Key, group => group.First().Key);

        var plans = new List<AkeneoCategoryBindingPlan>();

        foreach (var (code, akeneo) in akeneoByCode.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var parentCode = akeneo.Parent?.Trim();

            // A tree root is the Akeneo tree itself; nothing in nopCommerce stands for it.
            if (string.IsNullOrWhiteSpace(parentCode))
                continue;

            if (existingMappings.TryGetValue(code, out var mappedId))
            {
                plans.Add(liveNop.ContainsKey(mappedId)
                    ? new(code, AkeneoBindingStatus.AlreadyBound, mappedId, null)
                    : new(code, AkeneoBindingStatus.Issue, mappedId,
                        $"Mapped to nopCommerce category ID {mappedId}, which is missing or deleted. Remap it on the Category Mappings page."));
                continue;
            }

            if (!nopByCode.TryGetValue(code, out var candidates))
            {
                plans.Add(new(code, AkeneoBindingStatus.Unmatched, null, null));
                continue;
            }

            var parentIsRoot = akeneoByCode.TryGetValue(parentCode, out var akeneoParent) &&
                               string.IsNullOrWhiteSpace(akeneoParent.Parent);

            var matches = candidates
                .Where(candidate => parentIsRoot
                    ? candidate.ParentCategoryId == 0
                    : liveNop.TryGetValue(candidate.ParentCategoryId, out var nopParent) &&
                      string.Equals(CodeFor(nopParent.Name), parentCode, StringComparison.Ordinal))
                .ToList();

            if (matches.Count == 0)
            {
                plans.Add(new(code, AkeneoBindingStatus.Issue, null,
                    $"nopCommerce {Describe(candidates)} {(candidates.Count == 1 ? "has" : "have")} this name but a different parent " +
                    $"than Akeneo parent '{parentCode}'. Map it on the Category Mappings page if it is the same category."));
                continue;
            }

            // Several categories with the same name and parent: a single published one wins.
            if (matches.Count > 1)
            {
                var published = matches.Where(match => match.Published).ToList();
                if (published.Count != 1)
                {
                    plans.Add(new(code, AkeneoBindingStatus.Conflict, null,
                        $"Matches nopCommerce {Describe(matches)}. Map the right one on the Category Mappings page."));
                    continue;
                }

                matches = published;
            }

            var match = matches[0];

            if (boundTo.TryGetValue(match.Id, out var otherCode))
            {
                plans.Add(new(code, AkeneoBindingStatus.Issue, match.Id,
                    $"nopCommerce category {match.Id} '{match.Name}' is already bound to Akeneo category '{otherCode}'."));
                continue;
            }

            plans.Add(new(code, AkeneoBindingStatus.Matched, match.Id, null));
        }

        return plans;
    }

    private static string Describe(IReadOnlyCollection<Category> categories) =>
        (categories.Count == 1 ? "category " : "categories ") +
        string.Join(", ", categories.Select(category => $"{category.Id} '{category.Name}'"));
}
