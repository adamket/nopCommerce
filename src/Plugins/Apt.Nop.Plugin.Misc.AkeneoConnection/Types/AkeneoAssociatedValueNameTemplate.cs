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

    // Legacy tokens, matched only as whole single-brace tokens (never inside
    // a {{ }} literal escape).
    private static readonly Regex LegacyAxesToken = new(
        @"(?<!\{)\{\s*axes\s*\}(?!\})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex LegacyAxisToken = new(
        @"(?<!\{)\{\s*axis\s*:\s*(?<code>[^{}:\s]+)\s*\}(?!\})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex LegacyAttributeToken = new(
        @"(?<!\{)\{\s*attribute\s*:\s*(?<code>[^{}:\s]+)\s*\}(?!\})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Converts a name template into the value-template language used by
    /// computed mappings (conditionals, {attribute.field} reference fields,
    /// built-ins such as {sku}), keeping the legacy tokens working:
    /// {attribute:code} becomes {attr:code}; {axes} and {axis:code} are
    /// replaced with the variant's axis values as literal text.
    /// </summary>
    /// <param name="axisValues">
    /// Axis values to substitute; null when only validating, in which case
    /// axis tokens become a placeholder so the template still has content.
    /// </param>
    public static string ToValueTemplate(
        string template,
        IReadOnlyList<(string AxisCode, string DisplayValue)> axisValues)
    {
        if (string.IsNullOrWhiteSpace(template))
            template = Default;

        string Literal(string value) =>
            (value ?? string.Empty).Replace("{", "{{").Replace("}", "}}");

        var converted = LegacyAxesToken.Replace(template, _ => axisValues == null
            ? "axes"
            : Literal(string.Join(AxesJoin, axisValues
                .Select(axis => axis.DisplayValue)
                .Where(value => !string.IsNullOrWhiteSpace(value)))));

        converted = LegacyAxisToken.Replace(converted, match =>
        {
            if (axisValues == null)
                return "axis";

            var code = match.Groups["code"].Value;

            return Literal(axisValues
                .FirstOrDefault(axis => string.Equals(
                    axis.AxisCode,
                    code,
                    StringComparison.OrdinalIgnoreCase))
                .DisplayValue);
        });

        return LegacyAttributeToken.Replace(
            converted,
            match => "{attr:" + match.Groups["code"].Value + "}");
    }

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
