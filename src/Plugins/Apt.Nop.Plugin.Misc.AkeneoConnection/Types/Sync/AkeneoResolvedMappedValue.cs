using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;

public sealed class AkeneoResolvedMappedValue
{
    public AkeneoAttributeMapping Mapping { get; init; }

    public NopTargetType TargetType =>
        (NopTargetType)Mapping.NopTargetTypeId;

    public bool HasValue { get; init; }

    public AkeneoResolvedProductValue Value { get; init; }

    /// <summary>
    /// The primary or fallback Akeneo source that supplied the selected value.
    /// </summary>
    public string ResolvedSourceDisplayName { get; init; }

    public string DisplayValue =>
        HasValue
            ? Value?.DisplayValue ?? string.Empty
            : string.Empty;
}