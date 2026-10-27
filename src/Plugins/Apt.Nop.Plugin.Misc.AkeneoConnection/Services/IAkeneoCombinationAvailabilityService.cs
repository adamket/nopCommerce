using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Nop.Core.Domain.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public interface IAkeneoCombinationAvailabilityService
{
    Task<bool> ManagesEnabledStatusAsync(AkeneoProductDefinition source, CancellationToken cancellationToken = default);

    /// <summary>What decides the leaf's combination availability, or null when its family does not manage it.</summary>
    Task<AkeneoCombinationAvailabilitySource?> GetAvailabilitySourceAsync(AkeneoProductDefinition source, CancellationToken cancellationToken = default);

    Task ValidateRulesAsync(IEnumerable<string> familyCodes, AkeneoProductEnabledFilter enabledFilter, bool allowUpdates);

    Task<string> GetConfigurationFingerprintAsync(IEnumerable<string> familyCodes);

    Task<bool> IsAvailableAsync(AkeneoVariantImportContext context, CancellationToken cancellationToken = default);

    Task<AkeneoUnavailableCombinationPlan> GetUnavailableBindingAsync(
        AkeneoProductDefinition source, AkeneoProductImportRequest request, CancellationToken cancellationToken = default);

    Task<bool> RemoveAsync(Product parent, AkeneoVariantImportContext context, CancellationToken cancellationToken = default);
}

public sealed record AkeneoUnavailableCombinationPlan(
    AkeneoProductSyncState State, Product Parent, AkeneoVariantImportContext Context, bool CombinationExists);
