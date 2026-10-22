using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Seo;
using Nop.Data;
using Nop.Services.Catalog;
using Nop.Services.Seo;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public partial class AkeneoProductSeoSynchronizer
    : IAkeneoProductSectionSynchronizer,
      IAkeneoSectionDryRunPlanProvider
{
    private readonly IProductService _productService;
    private readonly IRepository<Product> _productRepository;
    private readonly IUrlRecordService _urlRecordService;
    private readonly SeoSettings _seoSettings;
    private readonly IRepository<UrlRecord> _urlRecordRepository;

    public AkeneoProductSeoSynchronizer(
        IProductService productService,
        IRepository<Product> productRepository,
        IUrlRecordService urlRecordService,
        SeoSettings seoSettings = null,
        IRepository<UrlRecord> urlRecordRepository = null)
    {
        _productService = productService;
        _productRepository = productRepository;
        _urlRecordService = urlRecordService;
        _seoSettings = seoSettings ?? new SeoSettings();
        _urlRecordRepository = urlRecordRepository;
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
            cancellationToken);

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
