using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public interface IAkeneoProductModelDeltaFanOutService
{
    bool ShouldRun(AkeneoProductBatchImportRequest request);

    string BuildDirectProductSearchJson(AkeneoProductBatchImportRequest request);

    string BuildLinkedAssetProductSearchJson(AkeneoProductBatchImportRequest request);

    string BuildDescendantProductSearchJson(
        AkeneoProductBatchImportRequest request,
        IEnumerable<string> parentProductModelCodes);

    Task<AkeneoProductModelDeltaFanOutPlan> BuildPlanAsync(
        AkeneoProductBatchImportRequest request,
        CancellationToken cancellationToken = default);
}
