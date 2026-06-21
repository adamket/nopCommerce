using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;
public interface IAkeneoProductBatchImportRequestFactory
{
    AkeneoProductBatchImportRequest CreateFromProfile(
        AkeneoSyncProfile profile,
        int syncRunRecordId);
}
