using System.Text.RegularExpressions;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types;

/// <summary>
/// Builds the generated product attribute value name for
/// AssociatedToProduct mode.
///
/// Supported tokens:
///   {axes}                   all axis display values, joined by " / "
///   {axis:code}              one axis display value by Akeneo attribute code
///   {sku}                    the nopCommerce SKU assigned to the variant
///   {identifier}             the Akeneo identifier for the variant
///   {attribute:code}         any value on the leaf Akeneo variant
///
/// Unknown tokens are left literal. Missing axis or attribute values render empty.
/// </summary>
public static class AkeneoAssociatedValueNameTemplate
{
    public const string Default = "{axes}";
    private const string AxesJoin = " / ";

    private static readonly Regex TokenPattern =
        new(@"\{(?<token>[^}]+)\}", RegexOptions.Compiled);

    public static string Render(
        string template,
        IReadOnlyList<(string AxisCode, string DisplayValue)> axisValues,
        string sku,
        string identifier,
        Func<string, string> attributeValueResolver = null)
    {
        if (string.IsNullOrWhiteSpace(template))
            template = Default;

        axisValues ??= Array.Empty<(string AxisCode, string DisplayValue)>();

        return TokenPattern.Replace(template, match =>
        {
            var token = match.Groups["token"].Value.Trim();

            if (token.Equals("axes", StringComparison.OrdinalIgnoreCase))
            {
                return string.Join(AxesJoin, axisValues
                    .Select(axis => axis.DisplayValue)
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
            }

            if (token.Equals("sku", StringComparison.OrdinalIgnoreCase))
                return sku ?? string.Empty;

            if (token.Equals("identifier", StringComparison.OrdinalIgnoreCase))
                return identifier ?? string.Empty;

            if (token.StartsWith("axis:", StringComparison.OrdinalIgnoreCase))
            {
                var code = token["axis:".Length..].Trim();

                if (string.IsNullOrWhiteSpace(code))
                    return string.Empty;

                return axisValues
                    .FirstOrDefault(axis =>
                        string.Equals(
                            axis.AxisCode,
                            code,
                            StringComparison.OrdinalIgnoreCase))
                    .DisplayValue ?? string.Empty;
            }

            if (token.StartsWith("attribute:", StringComparison.OrdinalIgnoreCase))
            {
                var code = token["attribute:".Length..].Trim();

                if (string.IsNullOrWhiteSpace(code) || attributeValueResolver == null)
                    return string.Empty;

                return attributeValueResolver(code) ?? string.Empty;
            }

            return match.Value;
        });
    }

    public static bool TryValidate(string template, out string error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(template))
            return true;

        var open = template.Count(character => character == '{');
        var close = template.Count(character => character == '}');

        if (open != close)
        {
            error = "Template has unbalanced { } braces.";
            return false;
        }

        foreach (Match match in TokenPattern.Matches(template))
        {
            var token = match.Groups["token"].Value.Trim();

            if (token.Equals("axis:", StringComparison.OrdinalIgnoreCase))
            {
                error = "The {axis:code} token must include an Akeneo axis attribute code.";
                return false;
            }

            if (token.Equals("attribute:", StringComparison.OrdinalIgnoreCase))
            {
                error = "The {attribute:code} token must include an Akeneo attribute code.";
                return false;
            }
        }

        return true;
    }
}
