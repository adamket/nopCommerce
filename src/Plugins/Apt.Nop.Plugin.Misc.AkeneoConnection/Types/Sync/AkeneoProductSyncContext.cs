using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Nop.Core.Domain.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;

public sealed class AkeneoProductSyncContext
{
    public AkeneoProductDefinition Source { get; init; }

    public AkeneoEntityType SourceEntityType { get; init; }

    public AkeneoProductImportRequest Request { get; init; }

    public AkeneoProductImportResult Result { get; init; }

    public IReadOnlyList<AkeneoResolvedMappedValue>
        MappedValues
    { get; init; }
        = Array.Empty<AkeneoResolvedMappedValue>();

    public string SourceCode { get; init; }

    public string SourceUuid { get; init; }

    public string ProductKey { get; init; }

    public string Sku { get; init; }

    public Product ExistingProduct { get; init; }

    public AkeneoProductSyncState ExistingSyncState { get; init; }

    public Product Product { get; set; }

    public bool ProductCreated { get; set; }

    public bool HasChanges { get; private set; }

    public bool StopProcessing { get; set; }

    public IEnumerable<AkeneoResolvedMappedValue> GetMappings(
        NopTargetType targetType)
    {
        return MappedValues.Where(value =>
            value.TargetType == targetType);
    }

    public void MarkChanged()
    {
        HasChanges = true;
    }
}