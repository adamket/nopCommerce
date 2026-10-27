using Nop.Core;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;

/// <summary>
/// Copies one Akeneo category value (a category template attribute or the
/// category label) into one nopCommerce category property for every mapped
/// category. Akeneo owns a mapped property: a sync overwrites edits made in
/// nopCommerce. One row per target and language.
/// </summary>
public class AkeneoCategoryContentMapping : BaseEntity
{
    public int TargetId { get; set; }

    public AkeneoCategoryContentTarget Target
    {
        get => (AkeneoCategoryContentTarget)TargetId;
        set => TargetId = (int)value;
    }

    /// <summary>
    /// 0 writes the category's own property. A language ID writes that
    /// language's localized value (or slug) instead.
    /// </summary>
    public int LanguageId { get; set; }

    public int SourceTypeId { get; set; }

    public AkeneoCategoryContentSourceType SourceType
    {
        get => (AkeneoCategoryContentSourceType)SourceTypeId;
        set => SourceTypeId = (int)value;
    }

    /// <summary>
    /// The template attribute code without Akeneo's "|uuid" suffix, so the
    /// mapping survives a template being recreated. Null for the label.
    /// </summary>
    public string AkeneoAttributeCode { get; set; }

    /// <summary>The Akeneo locale to read; null for a non-localizable value.</summary>
    public string Locale { get; set; }

    /// <summary>The Akeneo channel to read; null for a non-scopable value.</summary>
    public string Channel { get; set; }

    public int EmptyValueHandlingId { get; set; } = (int)AkeneoCategoryContentEmptyValueHandling.Keep;

    public AkeneoCategoryContentEmptyValueHandling EmptyValueHandling
    {
        get => (AkeneoCategoryContentEmptyValueHandling)EmptyValueHandlingId;
        set => EmptyValueHandlingId = (int)value;
    }

    public DateTime CreatedOnUtc { get; set; }

    public DateTime UpdatedOnUtc { get; set; }
}

public enum AkeneoCategoryContentTarget
{
    Name = 10,
    Description = 20,
    Picture = 30,
    MetaTitle = 40,
    MetaDescription = 50,
    MetaKeywords = 60,
    SeName = 70
}

public enum AkeneoCategoryContentSourceType
{
    /// <summary>The category label in <see cref="AkeneoCategoryContentMapping.Locale"/>.</summary>
    Label = 10,

    /// <summary>A category template attribute value.</summary>
    TemplateAttribute = 20
}

public enum AkeneoCategoryContentEmptyValueHandling
{
    /// <summary>An empty Akeneo value leaves the nopCommerce value alone.</summary>
    Keep = 10,

    /// <summary>
    /// An empty Akeneo value clears the nopCommerce value. A cleared slug is
    /// rebuilt from the category name; a name is never cleared.
    /// </summary>
    Clear = 20
}
