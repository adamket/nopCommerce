using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;

/// <summary>
/// Fixed rule: the sync never creates nopCommerce data for an Akeneo product
/// that is not 100% complete for the profile's channel and primary locale.
/// Products already on the site keep syncing whatever their completeness.
/// Product models have no completeness, and a product Akeneo returned no
/// completeness for (channel or locale not rated) is not blocked.
/// </summary>
public static class AkeneoIncompleteProductRule
{
    /// <summary>
    /// The warning to log instead of creating the product, or null when the
    /// product is complete or its completeness is unknown.
    /// </summary>
    public static string GetCreationBlockReason(AkeneoProductDefinition product, string channel, string locale)
    {
        var entry = product?.Completenesses?.FirstOrDefault(completeness =>
            string.Equals(completeness.Scope, channel?.Trim(), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(completeness.Locale, locale?.Trim(), StringComparison.OrdinalIgnoreCase));

        if (entry?.Data is not int percent || percent >= 100)
            return null;

        return $"Not imported: the product is {percent}% complete in Akeneo for channel '{channel?.Trim()}' " +
               $"and locale '{locale?.Trim()}', and incomplete products are never created in nopCommerce.";
    }
}
