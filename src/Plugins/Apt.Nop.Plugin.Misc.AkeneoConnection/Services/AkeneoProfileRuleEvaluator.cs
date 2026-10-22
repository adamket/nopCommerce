using System.Runtime.CompilerServices;
using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

/// <summary>
/// Profile rules use raw option codes and exact locale/channel matches. They
/// deliberately do not use label rendering or fallback to another storefront.
/// Ancestors are cached per request, never across independent runs.
/// </summary>
public sealed class AkeneoProfileRuleEvaluator(IAkeneoApiClient apiClient) : IAkeneoProfileRuleEvaluator
{
    private readonly ConditionalWeakTable<AkeneoProductImportRequest, Dictionary<string, AkeneoProductDefinition>>
        _modelCaches = new();

    public async Task<string> GetSkipReasonAsync(AkeneoProductDefinition source,
        AkeneoProductImportRequest request, CancellationToken cancellationToken = default)
    {
        var rules = AkeneoProfileRules.ParseSkipRules(request.SkipRulesJson);
        if (rules.Count == 0)
            return null;

        var chain = await GetChainAsync(source, request, cancellationToken);
        foreach (var rule in rules)
        {
            // A held parent also blocks descendants, even if a child overrides
            // the value: importing that child could otherwise modify its parent.
            for (var index = 0; index < chain.Count; index++)
            {
                var raw = ResolveRaw(chain.Skip(index), rule.AttributeCode, request);
                if (raw.HasValue && Matches(raw.Value, rule.Value))
                    return $"Skipped by profile rule: '{rule.AttributeCode}' equals '{rule.Value}' " +
                        $"on '{chain[index].Identifier ?? chain[index].Code ?? chain[index].Uuid}' " +
                        $"(channel '{request.Channel}', locale '{request.Locale}').";
            }
        }
        return null;
    }

    private async Task<IReadOnlyList<AkeneoProductDefinition>> GetChainAsync(
        AkeneoProductDefinition source, AkeneoProductImportRequest request, CancellationToken cancellationToken)
    {
        var chain = new List<AkeneoProductDefinition> { source };
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(source.Code)) visited.Add(source.Code);
        var cache = _modelCaches.GetOrCreateValue(request);
        var code = source.Parent?.Trim();
        while (!string.IsNullOrWhiteSpace(code))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!visited.Add(code))
                throw new InvalidOperationException($"Cannot evaluate profile rules: product-model cycle at '{code}'.");
            if (!cache.TryGetValue(code, out var model))
            {
                model = await apiClient.GetProductModelByCodeAsync(code, cancellationToken)
                    ?? throw new InvalidOperationException($"Cannot evaluate profile rules: parent product model '{code}' was not found.");
                cache[code] = model;
            }
            chain.Add(model);
            code = model.Parent?.Trim();
        }
        return chain;
    }

    private static JsonElement? ResolveRaw(IEnumerable<AkeneoProductDefinition> chain,
        string attribute, AkeneoProductImportRequest request)
    {
        foreach (var source in chain)
        {
            if (source.Values.ValueKind != JsonValueKind.Object ||
                !source.Values.TryGetProperty(attribute, out var entries) || entries.ValueKind != JsonValueKind.Array)
                continue;

            // Prefer exact scoped/localized entries over unscoped/unlocalized
            // ones. Null data explicitly on a child overrides an ancestor.
            var candidates = entries.EnumerateArray()
                .Where(entry => entry.ValueKind == JsonValueKind.Object &&
                    ContextMatches(entry, "scope", request.Channel) &&
                    ContextMatches(entry, "locale", request.Locale))
                .OrderByDescending(entry => Specificity(entry, "scope") + Specificity(entry, "locale"));
            foreach (var entry in candidates)
                if (entry.TryGetProperty("data", out var data)) return data.Clone();
        }
        return null;
    }

    private static bool ContextMatches(JsonElement entry, string key, string expected) =>
        !entry.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null ||
        (value.ValueKind == JsonValueKind.String && string.Equals(value.GetString(), expected, StringComparison.OrdinalIgnoreCase));

    private static int Specificity(JsonElement entry, string key) =>
        entry.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? 1 : 0;

    private static bool Matches(JsonElement value, string expected) => value.ValueKind == JsonValueKind.Array
        ? value.EnumerateArray().Any(item => string.Equals(Scalar(item), expected, StringComparison.OrdinalIgnoreCase))
        : string.Equals(Scalar(value), expected, StringComparison.OrdinalIgnoreCase);

    private static string Scalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Number => value.GetRawText(),
        _ => null
    };
}
