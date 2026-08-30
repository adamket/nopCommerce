using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;

/// <summary>
/// Immutable, source-derived synchronization intent. It contains the expensive
/// mapping/template/reference resolution for one effective Akeneo source role,
/// but deliberately excludes destination product state. A fresh destination
/// binding is created each time the intent is prepared for synchronization.
/// </summary>
public sealed class AkeneoResolvedProductIntent
{
    public AkeneoProductDefinition Source { get; init; }

    public AkeneoEntityType SourceEntityType { get; init; }

    public AkeneoProductImportRequest Request { get; init; }

    public IReadOnlyList<AkeneoResolvedMappedValue> MappedValues { get; init; }
        = Array.Empty<AkeneoResolvedMappedValue>();

    public string SourceCode { get; init; }

    public string SourceUuid { get; init; }

    public string MappingFamilyCode { get; init; }

    public string MappingFamilyVariantCode { get; init; }

    public AkeneoAttributeMappingEntityScope MappingEntityScope { get; init; }

    public string ProductKey { get; init; }

    public string Sku { get; init; }

    /// <summary>
    /// Diagnostics produced while resolving source-side mappings. These are
    /// replayed into each concrete import result that consumes this intent.
    /// </summary>
    public IReadOnlyList<string> Messages { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public bool Success => Errors.Count == 0;
}
