using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Tax;
using Nop.Data;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public partial class AkeneoProductCoreSynchronizer
    : IAkeneoProductSectionSynchronizer,
      IAkeneoSectionDryRunPlanProvider
{
    private readonly IProductService _productService;
    private readonly IRepository<Product> _productRepository;
    private readonly IAkeneoFamilyMappingService _familyMappingService;
    private readonly TaxSettings _taxSettings;

    public AkeneoProductCoreSynchronizer(
        IProductService productService,
        IRepository<Product> productRepository,
        IAkeneoFamilyMappingService familyMappingService = null,
        TaxSettings taxSettings = null)
    {
        _productService = productService;
        _productRepository = productRepository;
        _familyMappingService = familyMappingService;
        _taxSettings = taxSettings;
    }

    public int Order => 100;

    public async Task SynchronizeAsync(
        AkeneoProductSyncContext context,
        CancellationToken cancellationToken = default)
    {
        var isNew = context.ExistingProduct == null;
        var product = context.ExistingProduct ?? await CreateNewProductAsync(context);
        var plan = await BuildPlanAsync(
            context,
            product,
            isNew,
            cancellationToken);

        if (!plan.ImportAllowedByProfile)
        {
            context.Product = context.ExistingProduct;
            context.StopProcessing = true;
            context.Result.NopProductId = context.ExistingProduct?.Id ?? 0;
            context.Result.AddMessage(
                plan.ImportBlockReason ??
                "The saved sync profile does not allow this product write.");
            return;
        }

        var changed = await plan.ExecuteAsync(context, cancellationToken);

        if (isNew)
        {
            product.CreatedOnUtc = DateTime.UtcNow;
            product.UpdatedOnUtc = DateTime.UtcNow;

            await _productService.InsertProductAsync(product);

            context.ProductCreated = true;
            context.MarkChanged();
            context.Result.AddMessage(
                $"Created nopCommerce product ID {product.Id}.");
        }
        else if (changed)
        {
            product.UpdatedOnUtc = DateTime.UtcNow;
            // The pipeline publishes the update event once all sections have run.
            await _productRepository.UpdateAsync(product, publishEvent: false);
            context.ProductUpdateEventPending = true;

            context.Result.AddMessage(
                $"Updated core product fields for product ID {product.Id}.");
        }

        context.Product = product;
        context.Result.NopProductId = product.Id;
    }

    /// <summary>
    /// A product the sync creates, with the family mapping's new-product
    /// defaults: commerce settings Akeneo does not own. Updates never touch them.
    /// Without a family tax category it gets the store's default tax category,
    /// as a product created on the admin's new-product form does.
    /// </summary>
    private async Task<Product> CreateNewProductAsync(AkeneoProductSyncContext context)
    {
        var product = CreateBaseProduct(context);
        product.TaxCategoryId = _taxSettings?.DefaultTaxCategoryId ?? 0;

        var defaults = await GetNewProductDefaultsAsync(context);
        if (defaults == null)
            return product;

        product.IsShipEnabled = defaults.NewProductIsShipEnabled;
        if (defaults.NewProductTaxCategoryId is > 0)
            product.TaxCategoryId = defaults.NewProductTaxCategoryId.Value;
        if (defaults.NewProductWarehouseId is > 0)
            product.WarehouseId = defaults.NewProductWarehouseId.Value;
        if (defaults.NewProductTemplateId is > 0)
            product.ProductTemplateId = defaults.NewProductTemplateId.Value;

        return product;
    }

    private Task<AkeneoFamilyMapping> GetNewProductDefaultsAsync(AkeneoProductSyncContext context) =>
        _familyMappingService == null || string.IsNullOrWhiteSpace(context.MappingFamilyCode)
            ? Task.FromResult<AkeneoFamilyMapping>(null)
            : _familyMappingService.GetEffectiveMappingAsync(context.MappingFamilyCode, context.MappingFamilyVariantCode);

    private static Product CreateBaseProduct(
        AkeneoProductSyncContext context)
    {
        var fallbackName =
            context.Sku ??
            context.SourceCode ??
            context.ProductKey;

        return new Product
        {
            ProductType = ProductType.SimpleProduct,
            VisibleIndividually = true,
            Sku = fallbackName,
            Name = fallbackName,
            Published = context.GetMappings(Domain.NopTargetType.ProductField, includeSkipped: true)
                .Any(mapped => mapped.IsSkipped && string.Equals(mapped.Mapping.NopTargetKey, "Published", StringComparison.OrdinalIgnoreCase))
                ? false : context.Source.Enabled ?? false,
            CreatedOnUtc = DateTime.UtcNow,
            UpdatedOnUtc = DateTime.UtcNow
        };
    }

    private static bool SetIfChanged<T>(
        T current,
        T proposed,
        Action<T> setter)
    {
        if (EqualityComparer<T>.Default.Equals(current, proposed))
            return false;

        setter(proposed);
        return true;
    }

    private static bool TryParseBoolean(
        string value,
        out bool parsed)
    {
        if (bool.TryParse(value, out parsed))
            return true;

        if (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "y", StringComparison.OrdinalIgnoreCase))
        {
            parsed = true;
            return true;
        }

        if (string.Equals(value, "0", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "no", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "n", StringComparison.OrdinalIgnoreCase))
        {
            parsed = false;
            return true;
        }

        parsed = false;
        return false;
    }
}
