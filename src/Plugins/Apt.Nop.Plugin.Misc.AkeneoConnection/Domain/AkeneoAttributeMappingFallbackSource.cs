using Nop.Core;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;

/// <summary>
/// Defines an ordered fallback Akeneo source for an attribute mapping. The
/// mapping's own Akeneo source remains the primary source; these rows are tried
/// in DisplayOrder when the primary source has no non-empty value.
/// </summary>
public sealed class AkeneoAttributeMappingFallbackSource : BaseEntity
{
    public int AttributeMappingId { get; set; }

    public string AkeneoAttributeCode { get; set; }

    public int AkeneoAttributeTypeId { get; set; }

    public string AkeneoReferenceEntityCode { get; set; }

    public string AkeneoReferenceEntityAttributeCode { get; set; }

    public int DisplayOrder { get; set; }
}
