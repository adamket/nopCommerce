using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Services.Common;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public partial class AkeneoProductCustomPropertySynchronizer
    : IAkeneoProductSectionSynchronizer,
      IAkeneoSectionDryRunPlanProvider
{
    private readonly IGenericAttributeService _genericAttributeService;

    public AkeneoProductCustomPropertySynchronizer(
        IGenericAttributeService genericAttributeService)
    {
        _genericAttributeService = genericAttributeService;
    }

    public int Order => 600;

    public async Task SynchronizeAsync(
        AkeneoProductSyncContext context,
        CancellationToken cancellationToken = default)
    {
        if (context.Product is null)
            return;

        var plan = await BuildPlanAsync(context, cancellationToken);
        await plan.ExecuteAsync(context, cancellationToken);
    }
}
