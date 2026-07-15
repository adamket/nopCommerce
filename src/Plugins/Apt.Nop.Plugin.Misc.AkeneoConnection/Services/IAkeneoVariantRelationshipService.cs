using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Nop.Core.Domain.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
public interface IAkeneoVariantRelationshipService
{
    Task<AkeneoVariantSyncResult> ApplyAsync(
        Product parentProduct,
        AkeneoVariantImportContext context,
        Func<Task<Product>> upsertChildProductAsync);
}