using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public interface IAkeneoReferenceEntityValueResolver
{
    Task<AkeneoResolvedProductValue> ResolveAsync(
        AkeneoProductDefinition product,
        AkeneoAttributeMapping mapping,
        string locale = null,
        string channel = null,
        string currency = null,
        CancellationToken cancellationToken = default);
}
