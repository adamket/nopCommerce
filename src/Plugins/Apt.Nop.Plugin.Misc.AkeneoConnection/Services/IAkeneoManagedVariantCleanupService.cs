using Nop.Core.Domain.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public interface IAkeneoManagedVariantCleanupService
{
    Task DeleteCombinationAndUnusedAxisValuesAsync(
        int syncProfileId,
        ProductAttributeCombination combination,
        CancellationToken cancellationToken = default);
}
