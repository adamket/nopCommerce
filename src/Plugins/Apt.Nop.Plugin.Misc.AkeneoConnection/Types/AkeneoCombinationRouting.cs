using System.Text.Json;
using System.Text.RegularExpressions;
using Nop.Core;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types;

/// <summary>Opt-in routing for an existing parent's conditional selectors.</summary>
public sealed class AkeneoCombinationRouting
{
    public string ProductModelCode { get; set; }
    public int ParentProductId { get; set; }
    public bool AdoptExistingCombinations { get; set; }
    public bool ReconcileManagedCombinations { get; set; } = true;
    public List<AkeneoCombinationRoute> Routes { get; set; } = new();

    public static AkeneoCombinationRouting Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var configuration = JsonSerializer.Deserialize<AkeneoCombinationRouting>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (configuration == null) throw new NopException("Combination routing must be an object.");
            configuration.Validate();
            return configuration;
        }
        catch (JsonException ex) { throw new NopException($"Invalid combination routing JSON: {ex.Message}"); }
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ProductModelCode) || ParentProductId <= 0)
            throw new NopException("Combination routing requires a product model code and an existing parent product ID.");
        if (Routes == null || Routes.Count == 0 || Routes.Any(route => route == null))
            throw new NopException("Combination routing requires at least one route.");
        if (Routes.Select(route => route.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Routes.Count)
            throw new NopException("Combination route keys must be unique.");
        foreach (var route in Routes)
        {
            if (string.IsNullOrWhiteSpace(route.Key) || !Regex.IsMatch(route.Key, "^[a-zA-Z0-9_-]{1,80}$"))
                throw new NopException("Each route needs a stable key of up to 80 letters, digits, underscores or hyphens.");
            if (string.IsNullOrWhiteSpace(route.WhenAttribute) || string.IsNullOrWhiteSpace(route.WhenValue) ||
                string.IsNullOrWhiteSpace(route.AxisAttribute) || string.IsNullOrWhiteSpace(route.SelectorValue) ||
                route.SelectorAttributeId <= 0 || route.YearAttributeId <= 0 || route.SelectorAttributeId == route.YearAttributeId)
                throw new NopException($"Route '{route.Key}' requires a condition, source axis, selector value and two different destination attributes.");
            if (string.IsNullOrWhiteSpace(route.MembershipAttribute) != string.IsNullOrWhiteSpace(route.MembershipValue))
                throw new NopException($"Route '{route.Key}' requires both membership attribute and membership value, or neither.");
        }
        if (Routes.GroupBy(route => route.YearAttributeId).Any(group => group.Select(route =>
                (route.SelectorAttributeId, route.SelectorValue)).Distinct().Count() > 1))
            throw new NopException("A conditional year attribute cannot belong to different selector values.");
    }
}

public sealed class AkeneoCombinationRoute
{
    public string Key { get; set; }
    public string WhenAttribute { get; set; }
    public string WhenValue { get; set; }
    public string MembershipAttribute { get; set; }
    public string MembershipValue { get; set; }
    public int SelectorAttributeId { get; set; }
    public string SelectorValue { get; set; }
    public string AxisAttribute { get; set; }
    public int YearAttributeId { get; set; }
}
