using System.Text.RegularExpressions;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types;

/// <summary>
/// Builds the generated PAV name for AssociatedToProduct mode.
/// Tokens:
///   {axes}        all axis display values, joined by " / " in axis DisplayOrder
///   {axis:code}   the display value of a single axis by Akeneo attribute code
///   {identifier}  the variant's Akeneo identifier/SKU
/// Unknown {tokens} are left literal. Missing axis values render empty.
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
        string identifier)
    {
        if (string.IsNullOrWhiteSpace(template))
            template = Default;

        return TokenPattern.Replace(template, match =>
        {
            var token = match.Groups["token"].Value.Trim();

            if (token.Equals("axes", StringComparison.OrdinalIgnoreCase))
                return string.Join(AxesJoin, axisValues
                    .Select(a => a.DisplayValue)
                    .Where(v => !string.IsNullOrWhiteSpace(v)));

            if (token.Equals("identifier", StringComparison.OrdinalIgnoreCase))
                return identifier ?? string.Empty;

            if (token.StartsWith("axis:", StringComparison.OrdinalIgnoreCase))
            {
                var code = token["axis:".Length..].Trim();
                return axisValues
                    .FirstOrDefault(a => a.AxisCode.Equals(code, StringComparison.OrdinalIgnoreCase))
                    .DisplayValue ?? string.Empty;
            }

            return match.Value; // unknown token left literal
        });
    }

    public static bool TryValidate(string template, out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(template))
            return true; // falls back to Default

        var open = template.Count(c => c == '{');
        var close = template.Count(c => c == '}');
        if (open != close)
        {
            error = "Template has unbalanced { } braces.";
            return false;
        }

        return true;
    }
}