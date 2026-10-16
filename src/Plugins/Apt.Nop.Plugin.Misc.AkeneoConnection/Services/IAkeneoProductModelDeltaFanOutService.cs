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

    /// <summary>
    /// For a delta run, the reference entity records updated after the watermark, grouped by
    /// the product attribute that links them. Empty for any other run.
    /// </summary>
    Task<IReadOnlyList<AkeneoReferenceEntityRecordChange>> FindChangedReferenceEntityRecordsAsync(
        AkeneoProductBatchImportRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The profile's product search restricted to products whose <paramref name="attributeCode"/>
    /// links one of <paramref name="recordCodes"/>, without the updated-since criteria.
    /// </summary>
    string BuildReferenceEntityProductSearchJson(
        AkeneoProductBatchImportRequest request,
        string attributeCode,
        IEnumerable<string> recordCodes);
}

/// <summary>Records of <see cref="ReferenceEntityCode"/> that changed, linked through <see cref="AttributeCode"/>.</summary>
public sealed record AkeneoReferenceEntityRecordChange(
    string AttributeCode,
    string ReferenceEntityCode,
    IReadOnlyList<string> RecordCodes);
