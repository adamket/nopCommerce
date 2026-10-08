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
    /// Effective family-variant code resolved from the product-model hierarchy.
    /// Leaf product payloads do not expose family_variant directly.
    /// </summary>
    public string FamilyVariantCode { get; init; }

    /// <summary>
    /// The effective product SKU resolved from ordinary mappings, falling back
    /// to the Akeneo identifier or product-model code.
    /// </summary>
    public string Sku { get; init; }

    /// <summary>
    /// Pre-resolved values for <c>{attribute.field}</c> reference-entity tokens,
    /// keyed by <see cref="AkeneoTemplateReferenceField.Key"/>. Supplied by
    /// <c>RenderAsync</c>, which looks the linked records up before rendering.
    /// </summary>
    public IReadOnlyDictionary<string, AkeneoResolvedProductValue> ReferenceFieldValues { get; init; }

    public AkeneoValueTemplateContext WithReferenceFieldValues(
        IReadOnlyDictionary<string, AkeneoResolvedProductValue> referenceFieldValues) => new()
        {
            Source = Source,
            Locale = Locale,
            Channel = Channel,
            Currency = Currency,
            FamilyCode = FamilyCode,
            FamilyVariantCode = FamilyVariantCode,
            Sku = Sku,
            ReferenceFieldValues = referenceFieldValues
        };
}

/// <summary>
/// A <c>{attribute.field}</c> template token: a field of the reference-entity
/// record(s) linked by a product's reference-entity attribute.
/// </summary>
public sealed record AkeneoTemplateReferenceField(string AttributeCode, string FieldCode)
{
    public string Key => $"{AttributeCode}.{FieldCode}";
}

public sealed class AkeneoValueTemplateValidationResult
{
    public bool Success => Errors.Count == 0;

    public IReadOnlyList<string> ReferencedAttributeCodes { get; init; }
        = Array.Empty<string>();

    /// <summary>Reference-entity fields used as <c>{attribute.field}</c> tokens.</summary>
    public IReadOnlyList<AkeneoTemplateReferenceField> ReferenceFields { get; init; }
        = Array.Empty<AkeneoTemplateReferenceField>();

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
