using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Binding;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

/// <summary>
/// One nopCommerce category "Create missing categories" would create, or a reason it can't.
/// <see cref="ParentNopCategoryId"/> is 0 for a top-level category and null when the parent is
/// itself created in the same run (its ID is known only then).
/// </summary>
public sealed record AkeneoCategoryCreationPlan(
    string AkeneoCode,
    string Name,
    string ParentCode,
    int? ParentNopCategoryId,
    string Reason)
{
    public bool CanCreate => Reason == null;
}

/// <summary>
/// Decides which Akeneo categories binding can't match get created in nopCommerce. A category is
/// created when nothing in nopCommerce has its name, or when its parent is created in the same
/// run (nothing in nopCommerce can be under a category that doesn't exist yet). It goes under the
/// nopCommerce category its Akeneo parent is bound to, or at the top level for a category directly
/// under the tree root. Categories already mapped, matched, or in a conflict are never created: a
/// name found under another parent, or several matches, may be a category that was moved, and is
/// left for manual mapping. Results are parents first.
/// </summary>
public static class AkeneoCategoryCreationPlanner
{
    /// <param name="akeneoCategories">Every Akeneo category, roots included.</param>
    /// <param name="bindings">The binding plan for the same categories.</param>
    /// <param name="existingMappings">Akeneo category code to mapped nopCommerce category ID.</param>
    /// <param name="locale">The Akeneo label locale the nopCommerce name comes from.</param>
    public static IReadOnlyList<AkeneoCategoryCreationPlan> Plan(
        IReadOnlyCollection<AkeneoCategoryDefinition> akeneoCategories,
        IReadOnlyCollection<AkeneoCategoryBindingPlan> bindings,
        IReadOnlyDictionary<string, int> existingMappings,
        string locale = "en_US")
    {
        var akeneoByCode = akeneoCategories
            .Where(category => !string.IsNullOrWhiteSpace(category.Code))
            .GroupBy(category => category.Code.Trim(), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var bindingByCode = bindings.ToDictionary(binding => binding.AkeneoCode, StringComparer.Ordinal);

        // Akeneo category code to the live nopCommerce category it is (or will be) bound to.
        var bound = bindings
            .Where(binding => binding.NopCategoryId.HasValue &&
                              binding.Status is AkeneoBindingStatus.Matched or AkeneoBindingStatus.AlreadyBound)
            .ToDictionary(binding => binding.AkeneoCode, binding => binding.NopCategoryId.Value, StringComparer.Ordinal);

        var created = new HashSet<string>(StringComparer.Ordinal);
        var plans = new List<AkeneoCategoryCreationPlan>();

        foreach (var (code, akeneo) in akeneoByCode
                     .OrderBy(item => AkeneoCategoryBindingPlanner.Depth(item.Value, akeneoByCode))
                     .ThenBy(item => item.Key, StringComparer.Ordinal))
        {
            var parentCode = akeneo.Parent?.Trim();

            // A tree root is the Akeneo tree itself; nothing in nopCommerce stands for it.
            if (string.IsNullOrWhiteSpace(parentCode) || !bindingByCode.TryGetValue(code, out var binding))
                continue;

            var parentIsCreated = created.Contains(parentCode);
            var missing = binding.Status == AkeneoBindingStatus.Unmatched ||
                          (parentIsCreated &&
                           binding.Status == AkeneoBindingStatus.Issue &&
                           !existingMappings.ContainsKey(code));

            if (!missing)
            {
                // A mapping to a deleted category is not missing: it has to be removed or remapped first.
                if (binding.Status == AkeneoBindingStatus.Issue && existingMappings.ContainsKey(code))
                {
                    plans.Add(new(code, akeneo.GetLabel(locale), ParentOrNull(parentCode, akeneoByCode), null,
                        "Mapped to a nopCommerce category that is missing or deleted. Remove that mapping on the Category Mappings page to create it."));
                }

                continue;
            }

            var name = akeneo.GetLabel(locale);
            var parentIsRoot = akeneoByCode.TryGetValue(parentCode, out var akeneoParent) &&
                               string.IsNullOrWhiteSpace(akeneoParent.Parent);

            int? parentId = parentIsRoot
                ? 0
                : bound.TryGetValue(parentCode, out var boundParent)
                    ? boundParent
                    : null;

            if (parentId == null && !parentIsCreated)
            {
                plans.Add(new(code, name, parentCode, null,
                    $"Its parent '{parentCode}' has no nopCommerce category. Bind or create the parent first."));
                continue;
            }

            plans.Add(new(code, name, parentIsRoot ? null : parentCode, parentId, null));
            created.Add(code);
        }

        return plans;
    }

    private static string ParentOrNull(
        string parentCode,
        IReadOnlyDictionary<string, AkeneoCategoryDefinition> akeneoByCode) =>
        akeneoByCode.TryGetValue(parentCode, out var parent) && string.IsNullOrWhiteSpace(parent.Parent)
            ? null
            : parentCode;
}
