using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;

public sealed class AkeneoResolvedMappedValue
{
    public AkeneoAttributeMapping Mapping { get; init; }

    public NopTargetType TargetType =>
        (NopTargetType)Mapping.NopTargetTypeId;

    public bool HasValue { get; init; }

    public AkeneoResolvedProductValue Value { get; init; }

    public string DisplayValue =>
        HasValue
            ? Value?.DisplayValue ?? string.Empty
            : string.Empty;
}