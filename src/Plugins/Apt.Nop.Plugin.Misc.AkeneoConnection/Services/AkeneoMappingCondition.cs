using System.Text.RegularExpressions;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

/// <summary>
/// A small Boolean grammar over the template renderer's existing comparisons.
/// No dynamic code is evaluated. Every referenced comparison must resolve,
/// including those in an OR branch that would otherwise be short-circuited.
/// </summary>
public static class AkeneoMappingCondition
{
    private static readonly Regex Comparison = new(
        "^[a-zA-Z0-9_]+(?:[.:][a-zA-Z0-9_]+)*\\s*(?:==|!=)\\s*(?:\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*'|[-a-zA-Z0-9_.]+)$",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public static AkeneoValueTemplateValidationResult Validate(string expression, IAkeneoValueTemplateRenderer renderer)
    {
        if (string.IsNullOrWhiteSpace(expression)) return new();
        try
        {
            var leaves = Parse(expression).Leaves().ToList();
            var validations = leaves.Select(leaf => renderer.Validate(Template(leaf.Expression))).ToList();
            return new AkeneoValueTemplateValidationResult
            {
                Errors = validations.SelectMany(value => value.Errors).Distinct().ToList(),
                ReferencedAttributeCodes = validations.SelectMany(value => value.ReferencedAttributeCodes).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                ReferenceFields = validations.SelectMany(value => value.ReferenceFields).Distinct().ToList()
            };
        }
        catch (ArgumentException ex)
        {
            return new AkeneoValueTemplateValidationResult { Errors = new[] { ex.Message } };
        }
    }

    public static async Task<AkeneoMappingConditionResult> EvaluateAsync(string expression,
        IAkeneoValueTemplateRenderer renderer, AkeneoValueTemplateContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(expression)) return new(true, true, null);
        try
        {
            var node = Parse(expression);
            var values = new Dictionary<Node, bool>();
            foreach (var leaf in node.Leaves())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var rendered = await renderer.RenderAsync(Template(leaf.Expression), context, cancellationToken);
                if (!rendered.Success)
                {
                    var detail = rendered.Errors.Count > 0
                        ? string.Join(" ", rendered.Errors)
                        : "Missing or unreadable condition values: " + string.Join(", ", rendered.MissingTokens);
                    return new(false, false, detail);
                }
                if (!bool.TryParse(rendered.Value, out var value))
                    return new(false, false, "Condition did not resolve to a Boolean value.");
                values[leaf] = value;
            }
            return new(true, node.Evaluate(values), null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new(false, false, ex.Message);
        }
    }

    private static string Template(string comparison) => "{" + comparison + " ? \"true\" : \"false\"}";

    private static Node Parse(string expression)
    {
        if (expression.Length > 4000) throw new ArgumentException("Condition cannot exceed 4000 characters.");
        return ParseNode(expression.Trim(), 0);
    }

    private static Node ParseNode(string text, int nesting)
    {
        if (nesting > 16) throw new ArgumentException("Condition has too many nested groups.");
        text = text.Trim();
        if (text.Length == 0) throw new ArgumentException("Condition is missing a comparison.");
        var tokens = ScanLogicalOperators(text);
        // OR has lower precedence than AND. Quoted literals are never split.
        foreach (var isOr in new[] { true, false })
        {
            var splits = tokens.Where(token => token.IsOr == isOr).ToList();
            if (splits.Count == 0) continue;
            var children = new List<Node>();
            var start = 0;
            foreach (var split in splits)
            {
                children.Add(ParseNode(text[start..split.Offset], nesting + 1));
                start = split.Offset + split.Length;
            }
            children.Add(ParseNode(text[start..], nesting + 1));
            return new Node(null, isOr, children);
        }
        if (text.StartsWith('(') && text.EndsWith(')'))
            return ParseNode(text[1..^1], nesting + 1);
        if (!Comparison.IsMatch(text))
            throw new ArgumentException("Use comparisons such as sync_status == published, joined with AND or OR. Operators are == and !=; quote values containing spaces.");
        return new Node(text, false, Array.Empty<Node>());
    }

    private static List<LogicalToken> ScanLogicalOperators(string text)
    {
        var result = new List<LogicalToken>();
        var depth = 0;
        char? quote = null;
        var escaped = false;
        for (var index = 0; index < text.Length; index++)
        {
            var c = text[index];
            if (quote.HasValue)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == quote) quote = null;
                continue;
            }
            if (c is '\'' or '"') { quote = c; continue; }
            if (c == '(') { depth++; continue; }
            if (c == ')')
            {
                if (--depth < 0) throw new ArgumentException("Condition has unmatched parentheses.");
                continue;
            }
            if (depth != 0) continue;
            foreach (var op in new[] { "OR", "AND", "||", "&&" })
            {
                if (index + op.Length > text.Length ||
                    !text.AsSpan(index, op.Length).Equals(op, StringComparison.OrdinalIgnoreCase)) continue;
                var symbolic = op[0] is '|' or '&';
                if (!symbolic && ((index > 0 && !char.IsWhiteSpace(text[index - 1]) && text[index - 1] != ')') ||
                    (index + op.Length < text.Length && !char.IsWhiteSpace(text[index + op.Length]) && text[index + op.Length] != '('))) continue;
                result.Add(new LogicalToken(index, op.Length, op is "OR" or "||"));
                index += op.Length - 1;
                break;
            }
        }
        if (depth != 0 || quote.HasValue) throw new ArgumentException("Condition has unmatched parentheses or quotes.");
        return result;
    }

    private sealed record LogicalToken(int Offset, int Length, bool IsOr);
    private sealed record Node(string Expression, bool IsOr, IReadOnlyList<Node> Children)
    {
        public IEnumerable<Node> Leaves() => Expression != null ? new[] { this } : Children.SelectMany(child => child.Leaves());
        public bool Evaluate(IReadOnlyDictionary<Node, bool> values) => Expression != null
            ? values[this] : IsOr ? Children.Any(child => child.Evaluate(values)) : Children.All(child => child.Evaluate(values));
    }
}

public sealed record AkeneoMappingConditionResult(bool Success, bool Matches, string Error);
