using System.Text.Json;
namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
public class AkeneoResolvedProductValue
{
    public string AttributeCode { get; set; }

    public string Locale { get; set; }

    public string Channel { get; set; }

    public string Currency { get; set; }

    /// <summary>The Akeneo type supplied on the selected product value.</summary>
    public string SourceAttributeType { get; set; }

    /// <summary>
    /// Reference entity code supplied by Akeneo for reference-entity values.
    /// </summary>
    public string ReferenceDataName { get; set; }

    public JsonElement? RawData { get; set; }

    public string DisplayValue { get; set; }
    public IReadOnlyList<string> DisplayValues { get; set; }
}