using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Binding;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

/// <summary>
/// Binds Akeneo product models, variants and variant-axis options to the
/// nopCommerce products, combinations and attribute values that already
/// represent them. Bindings are global (not per sync profile).
/// </summary>
public interface IAkeneoCatalogBindingService
{
    /// <summary>
    /// Reads Akeneo and nopCommerce and reports what would be bound. Writes nothing.
    /// </summary>
    Task<AkeneoCatalogBindingReport> ScanAsync(
        IList<string> familyCodes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rescans, applies the chosen parent for each resolved conflict, and writes
    /// every unambiguous binding. Unchosen conflicts stay unbound.
    /// </summary>
    Task<AkeneoCatalogBindingReport> CommitAsync(
        IList<string> familyCodes,
        IDictionary<string, int> conflictChoices,
        CancellationToken cancellationToken = default);
}
