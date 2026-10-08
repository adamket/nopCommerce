using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

/// <summary>
/// Cheap, database-only check that lets an "update only" profile skip Akeneo
/// products with no nopCommerce counterpart before any mapping resolution or
/// asset download happens.
/// </summary>
public interface IAkeneoWritePolicyGate
{
    /// <summary>
    /// Returns a skip reason when neither the product nor any of its product
    /// models matches an existing nopCommerce product (so an update-only sync
    /// could only skip it), or null when the product must be processed.
    /// Errs toward null: anything that might exist is processed normally.
    /// </summary>
    Task<string> GetUpdateOnlySkipReasonAsync(
        AkeneoProductDefinition source,
        IReadOnlyList<AkeneoProductDefinition> ancestorsNearestFirst,
        CancellationToken cancellationToken = default);
}
