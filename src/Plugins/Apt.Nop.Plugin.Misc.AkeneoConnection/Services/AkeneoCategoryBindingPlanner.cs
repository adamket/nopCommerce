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
/// code, or the tree root for a top-level category. When two nopCommerce
/// categories give the same code, the migration keeps it for one and gives the
/// other its parent's code as a prefix ("accessories_socks"), or its
/// nopCommerce ID as a suffix ("socks_91") when there is no parent to prefix;
/// those forms match too, a plain code first. A match must also agree on the
/// parent: the nopCommerce category the Akeneo parent is mapped or bound to,
/// or, when the parent is not bound, a nopCommerce parent whose name gives the
/// Akeneo parent's code. Existing mappings are never changed, a nopCommerce
/// category is bound to one Akeneo category at most, and anything uncertain is
/// reported for manual mapping instead of bound.
/// </summary>
public static class AkeneoCategoryBindingPlanner
{
    private static readonly Regex NonAlphanumeric = new("[^a-z0-9]+", RegexOptions.Compiled);

    // Exact name codes are preferred over the migration's disambiguated forms.
    private enum CodeForm { Name, ParentPrefixed, IdSuffixed }

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

        var nopCodes = liveNop.Values
            .Select(category => (Code: CodeFor(category.Name), Category: category))
            .Where(item => item.Code != null)
            .ToList();

        // nopCommerce categories some Akeneo category is already bound to, including by this run.
        var boundTo = existingMappings
            .GroupBy(mapping => mapping.Value)
            .ToDictionary(group => group.Key, group => group.First().Key);

        // Akeneo category code to the live nopCommerce category it is mapped or bound to.
        var resolved = existingMappings
            .Where(mapping => liveNop.ContainsKey(mapping.Value))
            .ToDictionary(mapping => mapping.Key, mapping => mapping.Value, StringComparer.Ordinal);

        var plans = new List<AkeneoCategoryBindingPlan>();

        var work = akeneoByCode
            .Select(item => (
                Code: item.Key,
                ParentCode: item.Value.Parent?.Trim(),
                Depth: Depth(item.Value, akeneoByCode),
                Candidates: nopCodes
                    .Select(nop => (Form: Form(item.Key, item.Value.Parent?.Trim(), nop.Code, nop.Category.Id), nop.Category))
                    .Where(nop => nop.Form != null)
                    .ToList()))
            // Parents first, so a child is checked against what its parent was bound to. Within a
            // level, disambiguated codes go first: "gifts_2" claims its category before "gifts",
            // which then binds the other one instead of conflicting.
            .OrderBy(item => item.Depth)
            .ThenBy(item => item.Candidates.Any(candidate => candidate.Form != CodeForm.Name) ? 0 : 1)
            .ThenBy(item => item.Code, StringComparer.Ordinal);

        foreach (var (code, parentCode, _, candidates) in work)
        {
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

            if (candidates.Count == 0)
            {
                plans.Add(new(code, AkeneoBindingStatus.Unmatched, null, null));
                continue;
            }

            var parentIsRoot = akeneoByCode.TryGetValue(parentCode, out var akeneoParent) &&
                               string.IsNullOrWhiteSpace(akeneoParent.Parent);
            var parentId = resolved.TryGetValue(parentCode, out var boundParentId) ? boundParentId : (int?)null;

            var placed = candidates
                .Where(candidate => parentIsRoot
                    ? candidate.Category.ParentCategoryId == 0
                    : parentId is { } id
                        ? candidate.Category.ParentCategoryId == id
                        : liveNop.TryGetValue(candidate.Category.ParentCategoryId, out var nopParent) &&
                          string.Equals(CodeFor(nopParent.Name), parentCode, StringComparison.Ordinal))
                .ToList();
            var matches = placed.Count == 0
                ? []
                : placed.Where(candidate => candidate.Form == placed.Min(other => other.Form))
                    .Select(candidate => candidate.Category)
                    .ToList();

            if (matches.Count == 0)
            {
                var named = candidates.Select(candidate => candidate.Category).ToList();
                plans.Add(new(code, AkeneoBindingStatus.Issue, null,
                    $"nopCommerce {Describe(named)} {(named.Count == 1 ? "has" : "have")} this name but a different parent " +
                    $"than Akeneo parent '{parentCode}'. Map it on the Category Mappings page if it is the same category."));
                continue;
            }

            // A category already bound to another Akeneo category is never bound twice.
            var unbound = matches.Where(match => !boundTo.ContainsKey(match.Id)).ToList();
            if (unbound.Count == 0)
            {
                var taken = matches[0];
                plans.Add(new(code, AkeneoBindingStatus.Issue, taken.Id,
                    $"nopCommerce category {taken.Id} '{taken.Name}' is already bound to Akeneo category '{boundTo[taken.Id]}'."));
                continue;
            }

            matches = unbound;

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
            plans.Add(new(code, AkeneoBindingStatus.Matched, match.Id, null));
            boundTo[match.Id] = code;
            resolved[code] = match.Id;
        }

        return plans.OrderBy(plan => plan.AkeneoCode, StringComparer.Ordinal).ToList();
    }

    // How an Akeneo code relates to a nopCommerce category's name code, or null when it doesn't.
    private static CodeForm? Form(string akeneoCode, string parentCode, string nameCode, int nopId)
    {
        if (akeneoCode == nameCode)
            return CodeForm.Name;
        if (akeneoCode == $"{parentCode}_{nameCode}")
            return CodeForm.ParentPrefixed;
        // The suffix is the source site's ID, so it only matches when this is that site.
        if (akeneoCode == $"{nameCode}_{nopId}" || akeneoCode == $"{parentCode}_{nameCode}_{nopId}")
            return CodeForm.IdSuffixed;
        return null;
    }

    /// <summary>How many parents an Akeneo category has below its tree root (a top-level category is 1).</summary>
    internal static int Depth(
        AkeneoCategoryDefinition category,
        IReadOnlyDictionary<string, AkeneoCategoryDefinition> byCode)
    {
        var depth = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = category;
        while (!string.IsNullOrWhiteSpace(current.Parent) &&
               seen.Add(current.Code) &&
               byCode.TryGetValue(current.Parent.Trim(), out var parent))
        {
            depth++;
            current = parent;
        }

        return depth;
    }

    private static string Describe(IReadOnlyCollection<Category> categories) =>
        (categories.Count == 1 ? "category " : "categories ") +
        string.Join(", ", categories.Select(category => $"{category.Id} '{category.Name}'"));
}
