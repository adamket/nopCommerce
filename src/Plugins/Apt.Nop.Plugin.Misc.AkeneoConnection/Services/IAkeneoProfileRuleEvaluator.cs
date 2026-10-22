using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public interface IAkeneoProfileRuleEvaluator
{
    Task<string> GetSkipReasonAsync(AkeneoProductDefinition source,
        AkeneoProductImportRequest request, CancellationToken cancellationToken = default);

}
