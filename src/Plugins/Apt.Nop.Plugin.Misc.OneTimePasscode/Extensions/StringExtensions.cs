using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Apt.Nop.Plugin.Misc.OneTimePasscode.Extensions;
public static class StringExtensions
{
    private static readonly Regex _placeholderRegex = new(@"\{(\d+)\}", RegexOptions.Compiled);

    public static string SafeFormat(string? format, params object?[]? args)
    {
        if (string.IsNullOrEmpty(format))
            return format ?? string.Empty;

        args ??= Array.Empty<object?>();

        return _placeholderRegex.Replace(format, match =>
        {
            if (!int.TryParse(match.Groups[1].Value, out int index))
                return match.Value;

            if (index < 0 || index >= args.Length)
                return match.Value;

            return args[index]?.ToString() ?? string.Empty;
        });
    }
}
