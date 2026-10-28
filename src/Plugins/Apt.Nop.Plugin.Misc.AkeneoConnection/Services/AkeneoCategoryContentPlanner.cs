using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Categories;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

/// <summary>
/// The Akeneo side of one mapping for one category: its text, its image, or
/// why it cannot be used. No value at all leaves both empty.
/// </summary>
public sealed record AkeneoCategoryContentSource(
    string Text,
    AkeneoCategoryValue Image,
    string Problem)
{
    public static readonly AkeneoCategoryContentSource Empty = new(null, null, null);

    public bool HasValue => Text != null || Image != null;
}

/// <summary>What a text property should become; <see cref="Change"/> is false when it stays as it is.</summary>
public sealed record AkeneoCategoryContentTextDecision(bool Change, string Value);

/// <summary>
/// Pure decisions for the category content sync: which Akeneo value a mapping
/// reads and whether a nopCommerce text property changes. Slugs and pictures
/// need nopCommerce services and are decided by the sync service.
/// </summary>
public static class AkeneoCategoryContentPlanner
{
    /// <summary>Every target but the picture is text, and text can be stored per language.</summary>
    public static bool IsTextTarget(AkeneoCategoryContentTarget target) =>
        target != AkeneoCategoryContentTarget.Picture;

    public static AkeneoCategoryContentSource ResolveSource(
        AkeneoCategoryContentMapping mapping,
        AkeneoCategoryDefinition category,
        IReadOnlyList<AkeneoCategoryValue> values)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(category);

        if (mapping.SourceType == AkeneoCategoryContentSourceType.Label)
        {
            if (mapping.Target == AkeneoCategoryContentTarget.Picture)
                return new AkeneoCategoryContentSource(null, null, "A category label cannot supply a picture.");

            var locale = AkeneoCategoryValue.Normalize(mapping.Locale);
            var label = locale != null &&
                        category.Labels != null &&
                        category.Labels.TryGetValue(locale, out var localized)
                ? localized
                : null;

            return string.IsNullOrWhiteSpace(label)
                ? AkeneoCategoryContentSource.Empty
                : new AkeneoCategoryContentSource(label.Trim(), null, null);
        }

        var value = (values ?? [])
            .FirstOrDefault(candidate =>
                string.Equals(candidate.AttributeCode, mapping.AkeneoAttributeCode?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                candidate.Matches(mapping.Locale, mapping.Channel));

        if (value == null)
            return AkeneoCategoryContentSource.Empty;

        if (mapping.Target == AkeneoCategoryContentTarget.Picture)
        {
            if (!value.IsImage)
                return new AkeneoCategoryContentSource(null, null,
                    $"Akeneo attribute '{value.AttributeCode}' is not an image, so it cannot supply the picture.");

            return string.IsNullOrWhiteSpace(value.ImageFilePath)
                ? AkeneoCategoryContentSource.Empty
                : new AkeneoCategoryContentSource(null, value, null);
        }

        if (value.IsImage)
            return new AkeneoCategoryContentSource(null, null,
                $"Akeneo attribute '{value.AttributeCode}' is an image, so it cannot supply text.");

        var text = value.Text;
        if (string.IsNullOrWhiteSpace(text))
            return AkeneoCategoryContentSource.Empty;

        // Descriptions keep their HTML exactly; one-line values lose stray whitespace.
        return new AkeneoCategoryContentSource(
            mapping.Target == AkeneoCategoryContentTarget.Description ? text : text.Trim(),
            null,
            null);
    }

    /// <summary>
    /// Decides a text property. An empty Akeneo value changes nothing unless the
    /// mapping clears, and a name is never cleared. Slugs are not decided here.
    /// </summary>
    public static AkeneoCategoryContentTextDecision DecideText(
        AkeneoCategoryContentTarget target,
        AkeneoCategoryContentEmptyValueHandling emptyValueHandling,
        string current,
        string desired)
    {
        var currentValue = current ?? string.Empty;

        if (string.IsNullOrWhiteSpace(desired))
        {
            if (target == AkeneoCategoryContentTarget.Name ||
                emptyValueHandling != AkeneoCategoryContentEmptyValueHandling.Clear ||
                currentValue.Length == 0)
            {
                return new AkeneoCategoryContentTextDecision(false, currentValue);
            }

            return new AkeneoCategoryContentTextDecision(true, string.Empty);
        }

        return string.Equals(currentValue, desired, StringComparison.Ordinal)
            ? new AkeneoCategoryContentTextDecision(false, currentValue)
            : new AkeneoCategoryContentTextDecision(true, desired);
    }

    /// <summary>
    /// A standard-language text mapping that reads a locale is translated
    /// automatically: the same source is read in each translation locale.
    /// </summary>
    public static bool IsTranslatable(AkeneoCategoryContentMapping mapping) =>
        mapping != null &&
        mapping.LanguageId == 0 &&
        IsTextTarget(mapping.Target) &&
        !string.IsNullOrWhiteSpace(mapping.Locale);

    /// <summary>The standard mapping as it reads and writes one translation.</summary>
    public static AkeneoCategoryContentMapping ForTranslation(
        AkeneoCategoryContentMapping mapping,
        string locale,
        int languageId) => new()
    {
        Id = mapping.Id,
        TargetId = mapping.TargetId,
        LanguageId = languageId,
        SourceTypeId = mapping.SourceTypeId,
        AkeneoAttributeCode = mapping.AkeneoAttributeCode,
        Locale = locale,
        Channel = mapping.Channel,
        EmptyValueHandlingId = mapping.EmptyValueHandlingId
    };

    /// <summary>
    /// Decides an automatic translation, as for products: a value Akeneo does
    /// not have, or the same as the standard value, removes the translation so
    /// the storefront shows the standard value.
    /// </summary>
    public static AkeneoCategoryContentTextDecision DecideTranslation(
        string current,
        string translated,
        string standard)
    {
        var currentValue = current ?? string.Empty;
        var value = !string.IsNullOrWhiteSpace(translated) && !string.Equals(translated, standard, StringComparison.Ordinal)
            ? translated
            : string.Empty;

        return new AkeneoCategoryContentTextDecision(!string.Equals(currentValue, value, StringComparison.Ordinal), value);
    }

    /// <summary>
    /// Locale codes from a comma-, semicolon- or space-separated list, in
    /// Akeneo's underscore form (es-US becomes es_US), without duplicates.
    /// </summary>
    public static IList<string> ParseLocales(string value) =>
        (value ?? string.Empty)
            .Split([',', ';', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(locale => locale.Replace('-', '_'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Problems with a mapping's own settings, before any category is read.
    /// <paramref name="others"/> are the other saved mappings.
    /// </summary>
    public static IList<string> Validate(
        AkeneoCategoryContentMapping mapping,
        IEnumerable<AkeneoCategoryContentMapping> others)
    {
        var errors = new List<string>();

        if (!Enum.IsDefined(mapping.Target))
            errors.Add("Choose a nopCommerce category property.");
        if (!Enum.IsDefined(mapping.SourceType))
            errors.Add("Choose where the value comes from in Akeneo.");
        if (!Enum.IsDefined(mapping.EmptyValueHandling))
            errors.Add("Choose what happens when Akeneo has no value.");
        if (mapping.LanguageId < 0)
            errors.Add("The language is invalid.");

        if (errors.Count > 0)
            return errors;

        if (mapping.SourceType == AkeneoCategoryContentSourceType.TemplateAttribute &&
            string.IsNullOrWhiteSpace(mapping.AkeneoAttributeCode))
        {
            errors.Add("Choose the Akeneo category template attribute.");
        }

        if (mapping.SourceType == AkeneoCategoryContentSourceType.Label &&
            string.IsNullOrWhiteSpace(mapping.Locale))
        {
            errors.Add("Choose the locale of the Akeneo label.");
        }

        if (mapping.Target == AkeneoCategoryContentTarget.Picture)
        {
            if (mapping.SourceType != AkeneoCategoryContentSourceType.TemplateAttribute)
                errors.Add("The picture must come from an image attribute.");
            if (mapping.LanguageId != 0)
                errors.Add("nopCommerce categories have one picture for all languages; map it for the standard language.");
        }

        if (mapping.Target == AkeneoCategoryContentTarget.Name &&
            mapping.EmptyValueHandling == AkeneoCategoryContentEmptyValueHandling.Clear)
        {
            errors.Add("A category name cannot be cleared. Choose to keep the nopCommerce name when Akeneo has none.");
        }

        if ((others ?? []).Any(other =>
                other.Id != mapping.Id &&
                other.TargetId == mapping.TargetId &&
                other.LanguageId == mapping.LanguageId))
        {
            errors.Add("This property is already mapped for that language.");
        }

        return errors;
    }

    public static string DescribeTarget(AkeneoCategoryContentTarget target) => target switch
    {
        AkeneoCategoryContentTarget.Name => "Name",
        AkeneoCategoryContentTarget.Description => "Description",
        AkeneoCategoryContentTarget.Picture => "Picture",
        AkeneoCategoryContentTarget.MetaTitle => "Meta title",
        AkeneoCategoryContentTarget.MetaDescription => "Meta description",
        AkeneoCategoryContentTarget.MetaKeywords => "Meta keywords",
        AkeneoCategoryContentTarget.SeName => "Search engine friendly page name",
        _ => target.ToString()
    };
}
