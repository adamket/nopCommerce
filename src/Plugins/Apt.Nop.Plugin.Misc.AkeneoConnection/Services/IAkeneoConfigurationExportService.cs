namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

/// <summary>
/// Builds a redacted, diagnostic JSON snapshot of the persisted configuration
/// that controls the Akeneo connection and catalog synchronization behavior.
/// </summary>
public interface IAkeneoConfigurationExportService
{
    Task<byte[]> ExportAsync(
        int activeStoreScopeId,
        CancellationToken cancellationToken = default);
}
