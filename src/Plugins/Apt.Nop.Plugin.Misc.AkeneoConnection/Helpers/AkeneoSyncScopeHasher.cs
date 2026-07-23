using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Extensions;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;

public static class AkeneoSyncScopeHasher
{
    public static string Build(AkeneoSyncProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var scope = new
        {
            Channel = profile.AkeneoChannel?.Trim(),
            Locales = Normalize(profile.AkeneoLocales.SplitCsv()),
            Families = Normalize(profile.AkeneoFamilyCodes.SplitCsv()),
            Categories = Normalize(profile.AkeneoCategoryCodes.SplitCsv()),
            Groups = Normalize(profile.AkeneoProductGroupCodes.SplitCsv()),
            profile.CategoryFilterModeId,
            profile.ProductEnabledFilterId,
            profile.ProductParentFilterModeId,
            profile.IncludeLinkedAssetUpdates,
            AdditionalSearchJson = NormalizeJson(profile.AdditionalSearchJson)
        };

        var json = JsonSerializer.Serialize(scope);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static IList<string> Normalize(IEnumerable<string> values) =>
        values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList() ?? new List<string>();

    private static string NormalizeJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var document = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(document.RootElement);
        }
        catch
        {
            return json.Trim();
        }
    }
}
