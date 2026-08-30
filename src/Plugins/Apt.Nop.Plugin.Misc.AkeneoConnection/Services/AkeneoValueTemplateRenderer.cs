using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

/// <summary>
/// Renders deterministic text values from Akeneo root fields and attributes.
/// The parser intentionally supports a constrained token and conditional
/// language rather than evaluating arbitrary expressions.
/// </summary>
public sealed class AkeneoValueTemplateRenderer(
    IAkeneoProductValueResolver productValueResolver)
    : IAkeneoValueTemplateRenderer
{
    private static readonly ConcurrentDictionary<string, ParsedTemplate>
        ParsedTemplateCache = new(StringComparer.Ordinal);

    private static readonly Regex HorizontalWhitespaceRegex = new(
        @"[\t ]+",
        RegexOptions.Compiled);

    private static readonly Regex SpaceBeforePunctuationRegex = new(
        @"\s+([,.;:!?])",
        RegexOptions.Compiled);

    private static readonly HashSet<string> BuiltInTokens = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "sku",
        "code",
        "uuid",
        "identifier",
        "parent",
        "family",
        "family_variant",
        "familyVariant",
        "enabled"
    };

    public AkeneoValueTemplateValidationResult Validate(string template)
    {
        var parsed = Parse(template);

        return new AkeneoValueTemplateValidationResult
        {
            ReferencedAttributeCodes = GetReferencedAttributeCodes(parsed),
            Errors = parsed.Errors
        };
    }

    public AkeneoValueTemplateRenderResult Render(
        string template,
        AkeneoValueTemplateContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Source);

        var parsed = Parse(template);
        var referencedAttributeCodes = GetReferencedAttributeCodes(parsed);

        if (parsed.Errors.Count > 0)
        {
            return new AkeneoValueTemplateRenderResult
            {
                Errors = parsed.Errors,
                ReferencedAttributeCodes = referencedAttributeCodes
            };
        }

        var missingTokens = new List<string>();
        var output = new StringBuilder();

        AppendParsedTemplate(
            parsed,
            context,
            output,
            missingTokens);

        return new AkeneoValueTemplateRenderResult
        {
            Value = missingTokens.Count == 0
                ? NormalizeRenderedValue(output.ToString())
                : null,
            ReferencedAttributeCodes = referencedAttributeCodes,
            MissingTokens = missingTokens
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    private void AppendParsedTemplate(
        ParsedTemplate parsed,
        AkeneoValueTemplateContext context,
        StringBuilder output,
        ICollection<string> missingTokens)
    {
        foreach (var segment in parsed.Segments)
        {
            if (segment.Token != null)
            {
                var resolved = ResolveToken(segment.Token, context);

                if (string.IsNullOrWhiteSpace(resolved))
                {
                    missingTokens.Add(segment.Token.Original);
                    continue;
                }

                output.Append(resolved);
                continue;
            }

            if (segment.Conditional != null)
            {
                if (!TryEvaluateConditional(
                        segment.Conditional,
                        context,
                        out var conditionMatched))
                {
                    missingTokens.Add(
                        segment.Conditional.ConditionToken.Original);
                    continue;
                }

                var selectedBranch = conditionMatched
                    ? segment.Conditional.TrueTemplate
                    : segment.Conditional.FalseTemplate;

                AppendParsedTemplate(
                    selectedBranch,
                    context,
                    output,
                    missingTokens);
                continue;
            }

            output.Append(segment.Literal);
        }
    }

    private string ResolveToken(
        TemplateToken token,
        AkeneoValueTemplateContext context)
    {
        if (token.IsBuiltIn)
            return ResolveBuiltInToken(token.Name, context);

        if (!productValueResolver.TryGetValue(
                context.Source,
                token.AttributeCode,
                out var value,
                context.Locale,
                context.Channel,
                context.Currency))
        {
            return null;
        }

        return token.OutputMode == TemplateTokenOutputMode.Code
            ? FormatRawValue(value.RawData, context.Currency)
            : value.DisplayValue;
    }

    private bool TryEvaluateConditional(
        TemplateConditional conditional,
        AkeneoValueTemplateContext context,
        out bool conditionMatched)
    {
        conditionMatched = false;

        var values = ResolveComparisonValues(
            conditional.ConditionToken,
            context);

        if (values.Count == 0)
            return false;

        var equals = values.Any(value =>
            string.Equals(
                value?.Trim(),
                conditional.ComparisonValue,
                StringComparison.OrdinalIgnoreCase));

        conditionMatched = conditional.Operator switch
        {
            TemplateComparisonOperator.Equals => equals,
            TemplateComparisonOperator.NotEquals => !equals,
            _ => false
        };

        return true;
    }

    private IReadOnlyList<string> ResolveComparisonValues(
        TemplateToken token,
        AkeneoValueTemplateContext context)
    {
        if (token.IsBuiltIn)
        {
            var builtInValue = ResolveBuiltInToken(token.Name, context);

            return string.IsNullOrWhiteSpace(builtInValue)
                ? Array.Empty<string>()
                : new[] { builtInValue };
        }

        if (!productValueResolver.TryGetValue(
                context.Source,
                token.AttributeCode,
                out var value,
                context.Locale,
                context.Channel,
                context.Currency))
        {
            return Array.Empty<string>();
        }

        if (token.OutputMode == TemplateTokenOutputMode.Label)
        {
            return value.DisplayValues
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .ToList();
        }

        return FormatRawValues(value.RawData, context.Currency)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .ToList();
    }

    private static string ResolveBuiltInToken(
        string name,
        AkeneoValueTemplateContext context)
    {
        var source = context.Source;

        return name.KeyPart() switch
        {
            "sku" => context.Sku ?? source.Identifier ?? source.Code,
            "code" => source.Code ?? source.Identifier,
            "uuid" => source.Uuid,
            "identifier" => source.Identifier,
            "parent" => source.Parent,
            "family" => context.FamilyCode ?? source.Family,
            "family_variant" or "familyvariant" =>
                context.FamilyVariantCode ?? source.FamilyVariant,
            "enabled" => source.Enabled.HasValue
                ? source.Enabled.Value.ToString().ToLowerInvariant()
                : null,
            _ => null
        };
    }

    private static IReadOnlyList<string> FormatRawValues(
        JsonElement? rawData,
        string currency)
    {
        if (!rawData.HasValue)
            return Array.Empty<string>();

        var element = rawData.Value;

        if (element.ValueKind != JsonValueKind.Array)
        {
            var value = FormatRawValue(element, currency);

            return string.IsNullOrWhiteSpace(value)
                ? Array.Empty<string>()
                : new[] { value };
        }

        return element
            .EnumerateArray()
            .Select(item => FormatRawValue(item, currency))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
    }

    private static string FormatRawValue(
        JsonElement? rawData,
        string currency)
    {
        if (!rawData.HasValue)
            return null;

        var element = rawData.Value;

        return element.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Array => string.Join(
                ", ",
                element.EnumerateArray()
                    .Select(item => FormatRawValue(item, currency))
                    .Where(value => !string.IsNullOrWhiteSpace(value))),
            JsonValueKind.Object => FormatRawObject(element, currency),
            _ => element.GetRawText()
        };
    }

    private static string FormatRawObject(
        JsonElement element,
        string currency)
    {
        if (element.TryGetProperty("amount", out var amount))
        {
            var amountValue = amount.ValueKind == JsonValueKind.String
                ? amount.GetString()
                : amount.GetRawText();

            if (element.TryGetProperty("unit", out var unit))
            {
                var unitValue = unit.ValueKind == JsonValueKind.String
                    ? unit.GetString()
                    : null;

                return string.IsNullOrWhiteSpace(unitValue)
                    ? amountValue
                    : $"{amountValue} {unitValue}";
            }

            if (element.TryGetProperty("currency", out var objectCurrency))
            {
                var objectCurrencyValue = objectCurrency.ValueKind ==
                    JsonValueKind.String
                    ? objectCurrency.GetString()
                    : null;

                if (string.IsNullOrWhiteSpace(currency) ||
                    string.Equals(
                        currency,
                        objectCurrencyValue,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return amountValue;
                }

                return null;
            }
        }

        return element.GetRawText();
    }

    private static string NormalizeRenderedValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalizedLines = value
            .Replace("\r\n", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => HorizontalWhitespaceRegex
                .Replace(line, " ")
                .Trim())
            .Select(line => SpaceBeforePunctuationRegex
                .Replace(line, "$1"))
            .ToList();

        while (normalizedLines.Count > 0 &&
               string.IsNullOrWhiteSpace(normalizedLines[0]))
        {
            normalizedLines.RemoveAt(0);
        }

        while (normalizedLines.Count > 0 &&
               string.IsNullOrWhiteSpace(normalizedLines[^1]))
        {
            normalizedLines.RemoveAt(normalizedLines.Count - 1);
        }

        return string.Join(Environment.NewLine, normalizedLines);
    }

    private static IReadOnlyList<string> GetReferencedAttributeCodes(
        ParsedTemplate parsed)
    {
        return parsed.Tokens
            .Where(token => !token.IsBuiltIn)
            .Select(token => token.AttributeCode)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ParsedTemplate Parse(string template)
    {
        template ??= string.Empty;

        return ParsedTemplateCache.GetOrAdd(
            template,
            value => ParseUncached(
                value,
                allowConditionals: true,
                requireValueToken: true,
                contextDescription: "Template"));
    }

    private static ParsedTemplate ParseUncached(
        string template,
        bool allowConditionals,
        bool requireValueToken,
        string contextDescription)
    {
        var errors = new List<string>();
        var segments = new List<TemplateSegment>();
        var tokens = new List<TemplateToken>();
        var literal = new StringBuilder();

        for (var index = 0; index < template.Length; index++)
        {
            var character = template[index];

            if (character == '{' &&
                index + 1 < template.Length &&
                template[index + 1] == '{')
            {
                literal.Append('{');
                index++;
                continue;
            }

            if (character == '}' &&
                index + 1 < template.Length &&
                template[index + 1] == '}')
            {
                literal.Append('}');
                index++;
                continue;
            }

            if (character == '}')
            {
                errors.Add(
                    "Template contains a closing brace without a matching opening brace.");
                literal.Append(character);
                continue;
            }

            if (character != '{')
            {
                literal.Append(character);
                continue;
            }

            if (literal.Length > 0)
            {
                segments.Add(new TemplateSegment
                {
                    Literal = literal.ToString()
                });
                literal.Clear();
            }

            var closingBrace = FindClosingBrace(template, index + 1);

            if (closingBrace < 0)
            {
                errors.Add(
                    "Template contains an opening brace without a matching closing brace.");
                literal.Append(template[index..]);
                break;
            }

            var expressionText = template[(index + 1)..closingBrace].Trim();

            if (string.IsNullOrWhiteSpace(expressionText))
            {
                errors.Add("Template expressions cannot be empty.");
                index = closingBrace;
                continue;
            }

            var segment = ParseExpression(
                expressionText,
                errors,
                tokens,
                allowConditionals,
                contextDescription);

            if (segment != null)
                segments.Add(segment);

            index = closingBrace;
        }

        if (literal.Length > 0)
            segments.Add(new TemplateSegment { Literal = literal.ToString() });

        if (requireValueToken && tokens.Count == 0)
        {
            errors.Add(
                $"{contextDescription} must contain at least one value token.");
        }

        return new ParsedTemplate
        {
            Segments = segments,
            Tokens = tokens,
            Errors = errors.Distinct(StringComparer.Ordinal).ToList()
        };
    }

    private static int FindClosingBrace(
        string template,
        int startIndex)
    {
        char? quote = null;
        var escaped = false;
        var nestedBraceDepth = 0;

        for (var index = startIndex; index < template.Length; index++)
        {
            var character = template[index];

            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (quote.HasValue && character == '\\')
            {
                escaped = true;
                continue;
            }

            if (character is '\'' or '"')
            {
                if (!quote.HasValue)
                    quote = character;
                else if (quote.Value == character)
                    quote = null;

                continue;
            }

            if (quote.HasValue)
                continue;

            if (character == '{')
            {
                nestedBraceDepth++;
                continue;
            }

            if (character != '}')
                continue;

            if (nestedBraceDepth > 0)
            {
                nestedBraceDepth--;
                continue;
            }

            return index;
        }

        return -1;
    }

    private static TemplateSegment ParseExpression(
        string expressionText,
        IList<string> errors,
        ICollection<TemplateToken> tokens,
        bool allowConditionals,
        string contextDescription)
    {
        if (TrySplitTernary(
                expressionText,
                out var conditionText,
                out var trueText,
                out var falseText,
                out var ternaryError))
        {
            if (!allowConditionals)
            {
                errors.Add(
                    $"{contextDescription} contains a nested conditional expression '{{{expressionText}}}'. Nested conditionals are not supported.");
                return null;
            }

            var conditional = ParseConditional(
                expressionText,
                conditionText,
                trueText,
                falseText,
                errors);

            if (conditional == null)
                return null;

            tokens.Add(conditional.ConditionToken);

            foreach (var branchToken in conditional.TrueTemplate.Tokens)
                tokens.Add(branchToken);

            foreach (var branchToken in conditional.FalseTemplate.Tokens)
                tokens.Add(branchToken);

            return new TemplateSegment
            {
                Conditional = conditional
            };
        }

        if (!string.IsNullOrWhiteSpace(ternaryError))
        {
            errors.Add(ternaryError);
            return null;
        }

        var token = ParseToken(
            expressionText,
            errors,
            TemplateTokenOutputMode.Label);

        if (token == null)
            return null;

        tokens.Add(token);

        return new TemplateSegment { Token = token };
    }

    private static bool TrySplitTernary(
        string expressionText,
        out string conditionText,
        out string trueText,
        out string falseText,
        out string error)
    {
        conditionText = null;
        trueText = null;
        falseText = null;
        error = null;

        var questionIndex = -1;
        var colonIndex = -1;
        char? quote = null;
        var escaped = false;
        var braceDepth = 0;

        for (var index = 0; index < expressionText.Length; index++)
        {
            var character = expressionText[index];

            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (quote.HasValue && character == '\\')
            {
                escaped = true;
                continue;
            }

            if (character is '\'' or '"')
            {
                if (!quote.HasValue)
                    quote = character;
                else if (quote.Value == character)
                    quote = null;

                continue;
            }

            if (quote.HasValue)
                continue;

            if (character == '{')
            {
                braceDepth++;
                continue;
            }

            if (character == '}')
            {
                if (braceDepth > 0)
                    braceDepth--;

                continue;
            }

            if (braceDepth > 0)
                continue;

            if (character == '?')
            {
                if (questionIndex >= 0)
                {
                    error =
                        $"Conditional expression '{{{expressionText}}}' contains more than one ternary operator. Nested ternaries are not supported.";
                    return false;
                }

                questionIndex = index;
                continue;
            }

            if (character == ':' &&
                questionIndex >= 0 &&
                colonIndex < 0)
            {
                colonIndex = index;
            }
        }

        if (quote.HasValue)
        {
            error =
                $"Conditional expression '{{{expressionText}}}' contains an unterminated quoted value.";
            return false;
        }

        if (questionIndex < 0)
            return false;

        if (colonIndex < 0)
        {
            error =
                $"Conditional expression '{{{expressionText}}}' must include ':' between its true and false values.";
            return false;
        }

        conditionText = expressionText[..questionIndex].Trim();
        trueText = expressionText[(questionIndex + 1)..colonIndex].Trim();
        falseText = expressionText[(colonIndex + 1)..].Trim();

        if (string.IsNullOrWhiteSpace(conditionText) ||
            string.IsNullOrWhiteSpace(trueText) ||
            string.IsNullOrWhiteSpace(falseText))
        {
            error =
                $"Conditional expression '{{{expressionText}}}' must include a condition, true value, and false value.";
            return false;
        }

        return true;
    }

    private static TemplateConditional ParseConditional(
        string original,
        string conditionText,
        string trueText,
        string falseText,
        IList<string> errors)
    {
        if (!TrySplitComparison(
                conditionText,
                out var leftText,
                out var comparisonOperator,
                out var rightText,
                out var comparisonError))
        {
            errors.Add(
                comparisonError ??
                $"Conditional expression '{{{original}}}' must use '==' or '!='.");
            return null;
        }

        var conditionToken = ParseToken(
            leftText,
            errors,
            TemplateTokenOutputMode.Code);

        if (conditionToken == null)
            return null;

        if (!TryParseLiteral(
                rightText,
                "comparison value",
                original,
                errors,
                out var comparisonValue))
        {
            return null;
        }

        if (!TryParseBranchTemplate(
                trueText,
                "true value",
                original,
                errors,
                out var trueTemplate))
        {
            return null;
        }

        if (!TryParseBranchTemplate(
                falseText,
                "false value",
                original,
                errors,
                out var falseTemplate))
        {
            return null;
        }

        return new TemplateConditional
        {
            Original = original,
            ConditionToken = conditionToken,
            Operator = comparisonOperator,
            ComparisonValue = comparisonValue.Trim(),
            TrueTemplate = trueTemplate,
            FalseTemplate = falseTemplate
        };
    }

    private static bool TryParseBranchTemplate(
        string text,
        string valueDescription,
        string originalExpression,
        IList<string> errors,
        out ParsedTemplate parsedTemplate)
    {
        parsedTemplate = null;

        if (!TryParseLiteral(
                text,
                valueDescription,
                originalExpression,
                errors,
                out var branchTemplate))
        {
            return false;
        }

        parsedTemplate = ParseUncached(
            branchTemplate,
            allowConditionals: false,
            requireValueToken: false,
            contextDescription:
                $"Conditional expression '{{{originalExpression}}}' {valueDescription}");

        if (parsedTemplate.Errors.Count == 0)
            return true;

        foreach (var branchError in parsedTemplate.Errors)
            errors.Add(branchError);

        return false;
    }

    private static bool TrySplitComparison(
        string conditionText,
        out string leftText,
        out TemplateComparisonOperator comparisonOperator,
        out string rightText,
        out string error)
    {
        leftText = null;
        rightText = null;
        comparisonOperator = TemplateComparisonOperator.Equals;
        error = null;

        var operatorIndex = -1;
        var operatorLength = 0;
        char? quote = null;
        var escaped = false;

        for (var index = 0; index < conditionText.Length - 1; index++)
        {
            var character = conditionText[index];

            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (quote.HasValue && character == '\\')
            {
                escaped = true;
                continue;
            }

            if (character is '\'' or '"')
            {
                if (!quote.HasValue)
                    quote = character;
                else if (quote.Value == character)
                    quote = null;

                continue;
            }

            if (quote.HasValue)
                continue;

            var pair = conditionText.Substring(index, 2);

            if (pair is not ("==" or "!="))
                continue;

            if (operatorIndex >= 0)
            {
                error =
                    $"Conditional condition '{conditionText}' contains more than one comparison operator.";
                return false;
            }

            operatorIndex = index;
            operatorLength = 2;
            comparisonOperator = pair == "=="
                ? TemplateComparisonOperator.Equals
                : TemplateComparisonOperator.NotEquals;
            index++;
        }

        if (operatorIndex < 0)
            return false;

        leftText = conditionText[..operatorIndex].Trim();
        rightText = conditionText[(operatorIndex + operatorLength)..].Trim();

        if (string.IsNullOrWhiteSpace(leftText) ||
            string.IsNullOrWhiteSpace(rightText))
        {
            error =
                $"Conditional condition '{conditionText}' must include values on both sides of the comparison operator.";
            return false;
        }

        return true;
    }

    private static bool TryParseLiteral(
        string text,
        string valueDescription,
        string originalExpression,
        IList<string> errors,
        out string value)
    {
        value = null;
        text = text?.Trim();

        if (string.IsNullOrWhiteSpace(text))
        {
            errors.Add(
                $"Conditional expression '{{{originalExpression}}}' has an empty {valueDescription}.");
            return false;
        }

        if (text[0] is not ('\'' or '"'))
        {
            value = text;
            return true;
        }

        var quote = text[0];

        if (text.Length < 2 || text[^1] != quote)
        {
            errors.Add(
                $"Conditional expression '{{{originalExpression}}}' has an unterminated {valueDescription}.");
            return false;
        }

        var builder = new StringBuilder();
        var escaped = false;

        for (var index = 1; index < text.Length - 1; index++)
        {
            var character = text[index];

            if (!escaped && character == '\\')
            {
                escaped = true;
                continue;
            }

            if (!escaped)
            {
                if (character == quote)
                {
                    errors.Add(
                        $"Conditional expression '{{{originalExpression}}}' contains an unescaped quote in its {valueDescription}.");
                    return false;
                }

                builder.Append(character);
                continue;
            }

            builder.Append(character switch
            {
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                '\\' => '\\',
                '\'' => '\'',
                '"' => '"',
                _ => character
            });
            escaped = false;
        }

        if (escaped)
            builder.Append('\\');

        value = builder.ToString();
        return true;
    }

    private static TemplateToken ParseToken(
        string tokenText,
        IList<string> errors,
        TemplateTokenOutputMode defaultOutputMode)
    {
        var parts = tokenText
            .Split(':', StringSplitOptions.TrimEntries)
            .ToList();

        var explicitAttribute = parts.Count > 0 &&
            string.Equals(parts[0], "attr", StringComparison.OrdinalIgnoreCase);

        if (explicitAttribute)
            parts.RemoveAt(0);

        if (parts.Count == 0 || string.IsNullOrWhiteSpace(parts[0]))
        {
            errors.Add(
                $"Template token '{{{tokenText}}}' does not identify a value.");
            return null;
        }

        if (parts.Count > 2)
        {
            errors.Add(
                $"Template token '{{{tokenText}}}' contains too many modifiers.");
            return null;
        }

        var name = parts[0].Trim();
        var modifier = parts.Count == 2
            ? parts[1].KeyPart()
            : defaultOutputMode == TemplateTokenOutputMode.Code
                ? "code"
                : "label";

        if (modifier is not ("label" or "code"))
        {
            errors.Add(
                $"Template token '{{{tokenText}}}' uses unsupported modifier '{modifier}'. Use 'label' or 'code'.");
            return null;
        }

        var isBuiltIn = !explicitAttribute && BuiltInTokens.Contains(name);

        return new TemplateToken
        {
            Original = tokenText,
            Name = name,
            IsBuiltIn = isBuiltIn,
            AttributeCode = isBuiltIn ? null : name,
            OutputMode = modifier == "code"
                ? TemplateTokenOutputMode.Code
                : TemplateTokenOutputMode.Label
        };
    }

    private sealed class ParsedTemplate
    {
        public IReadOnlyList<TemplateSegment> Segments { get; init; }
            = Array.Empty<TemplateSegment>();

        public IReadOnlyList<TemplateToken> Tokens { get; init; }
            = Array.Empty<TemplateToken>();

        public IReadOnlyList<string> Errors { get; init; }
            = Array.Empty<string>();
    }

    private sealed class TemplateSegment
    {
        public string Literal { get; init; }

        public TemplateToken Token { get; init; }

        public TemplateConditional Conditional { get; init; }
    }

    private sealed class TemplateConditional
    {
        public string Original { get; init; }

        public TemplateToken ConditionToken { get; init; }

        public TemplateComparisonOperator Operator { get; init; }

        public string ComparisonValue { get; init; }

        public ParsedTemplate TrueTemplate { get; init; }

        public ParsedTemplate FalseTemplate { get; init; }
    }

    private sealed class TemplateToken
    {
        public string Original { get; init; }

        public string Name { get; init; }

        public bool IsBuiltIn { get; init; }

        public string AttributeCode { get; init; }

        public TemplateTokenOutputMode OutputMode { get; init; }
    }

    private enum TemplateTokenOutputMode
    {
        Label = 0,
        Code = 10
    }

    private enum TemplateComparisonOperator
    {
        Equals = 0,
        NotEquals = 10
    }
}
