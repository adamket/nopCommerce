using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Services.Catalog;
using Nop.Services.Seo;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public partial class AkeneoProductSeoSynchronizer
    : IAkeneoProductSectionSynchronizer,
      IAkeneoSectionDryRunPlanProvider
{
    private readonly IProductService _productService;
    private readonly IUrlRecordService _urlRecordService;

    public AkeneoProductSeoSynchronizer(
        IProductService productService,
        IUrlRecordService urlRecordService)
    {
        _productService = productService;
        _urlRecordService = urlRecordService;
    }

    public int Order => 200;

    public async Task SynchronizeAsync(
        AkeneoProductSyncContext context,
        CancellationToken cancellationToken = default)
    {
        if (context.Product is null)
            return;

        var plan = await BuildPlanAsync(
            context,
            previewModel: null,
            cancellationToken: cancellationToken);

        await plan.ExecuteAsync(context, cancellationToken);
    }

    private static bool SetIfChanged(
        string current,
        string proposed,
        Action<string> setter)
    {
        if (string.Equals(current, proposed, StringComparison.Ordinal))
            return false;

        setter(proposed);
        return true;
    }
}
