using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types;

public sealed class AkeneoValueTemplateContext
{
    public AkeneoProductDefinition Source { get; init; }

    public string Locale { get; init; }

    public string Channel { get; init; }

    public string Currency { get; init; }

    /// <summary>
    /// Family used to select the mapping scope. Product-model payloads do not
    /// normally contain a family, so this value is supplied by the leaf product.
    /// </summary>
    public string FamilyCode { get; init; }

    /// <summary>
    /// The effective product SKU resolved from ordinary mappings, falling back
    /// to the Akeneo identifier or product-model code.
    /// </summary>
    public string Sku { get; init; }
}

public sealed class AkeneoValueTemplateValidationResult
{
    public bool Success => Errors.Count == 0;

    public IReadOnlyList<string> ReferencedAttributeCodes { get; init; }
        = Array.Empty<string>();

    public IReadOnlyList<string> Errors { get; init; }
        = Array.Empty<string>();
}

public sealed class AkeneoValueTemplateRenderResult
{
    public bool Success => Errors.Count == 0 && MissingTokens.Count == 0;

    public string Value { get; init; }

    public IReadOnlyList<string> ReferencedAttributeCodes { get; init; }
        = Array.Empty<string>();

    public IReadOnlyList<string> MissingTokens { get; init; }
        = Array.Empty<string>();

    public IReadOnlyList<string> Errors { get; init; }
        = Array.Empty<string>();
}
