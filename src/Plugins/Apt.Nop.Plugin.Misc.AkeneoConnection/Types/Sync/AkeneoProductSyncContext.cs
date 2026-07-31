using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api;
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

    /// <summary>
    /// Family code used to resolve family-scoped mappings and family variant
    /// configuration. This may differ from Source.Family for product models,
    /// because Akeneo product-model payloads do not include a family field.
    /// </summary>
    public string MappingFamilyCode { get; init; }

    /// <summary>
    /// The nopCommerce product role used to filter attribute mappings for this
    /// synchronization context.
    /// </summary>
    public AkeneoAttributeMappingEntityScope MappingEntityScope { get; init; }

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

    // Settable so a single item-scoped cache can be shared across the parent
    // and every child/leaf context, letting a pre-transaction prepare pass
    // populate binaries that the in-transaction write pass reads back.
    public IDictionary<string, AkeneoBinaryFile> PreloadedAssetBinaries { get; set; }
        = new Dictionary<string, AkeneoBinaryFile>(StringComparer.OrdinalIgnoreCase);
}