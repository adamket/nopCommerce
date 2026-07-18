using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public interface IAkeneoValueTransformationService
{
    AkeneoTransformationResult Transform(
        AkeneoResolvedProductValue source,
        AkeneoAttributeMapping mapping);
}

public sealed class AkeneoTransformationResult
{
    public AkeneoResolvedProductValue Value { get; init; }

    public string Error { get; init; }

    public bool Success => string.IsNullOrWhiteSpace(Error);
}
