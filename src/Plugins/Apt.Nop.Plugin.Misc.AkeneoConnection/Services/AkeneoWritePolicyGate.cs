using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

/// <summary>
/// Looks for any nopCommerce entity an Akeneo product could update, using the
/// same identities the sync uses: plugin bindings (product, combination and
/// product-model mappings) and SKU matches (product, combination, and the
/// product-model code the sync uses as a parent SKU).
/// </summary>
public class AkeneoWritePolicyGate(
    IAkeneoNopEntityMappingService entityMappingService,
    IAkeneoAttributeMappingService attributeMappingService,
    IProductService productService,
    IProductAttributeService productAttributeService)
    : IAkeneoWritePolicyGate
{
    // Scoped service: one instance per run, so this caches per run.
    private readonly Dictionary<string, bool> _familyHasSkuMapping =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task<string> GetUpdateOnlySkipReasonAsync(
        AkeneoProductDefinition source,
        IReadOnlyList<AkeneoProductDefinition> ancestorsNearestFirst,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        // A Sku mapped from anything other than the identifier means the sync
        // may match by a SKU this gate cannot see without resolving mappings,
        // so never skip early.
        if (await HasSkuMappingAsync(source.Family))
            return null;

        cancellationToken.ThrowIfCancellationRequested();

        if (await ProductExistsAsync(source))
            return null;

        var models = (ancestorsNearestFirst ?? Array.Empty<AkeneoProductDefinition>())
            .Where(model => !string.IsNullOrWhiteSpace(model?.Code))
            .ToList();

        foreach (var model in models)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await ProductModelExistsAsync(model.Code.Trim()))
                return null;
        }

        return models.Count == 0
            ? "Skipped: no nopCommerce product matches this Akeneo product, and the sync profile only updates existing products."
            : "Skipped: neither this variant nor its product models (" +
              string.Join(", ", models.Select(model => $"'{model.Code.Trim()}'")) +
              ") match a nopCommerce product, and the sync profile only updates existing products.";
    }

    private async Task<bool> ProductExistsAsync(AkeneoProductDefinition source)
    {
        var uuid = source.Uuid?.Trim();
        var identifier = source.Identifier?.Trim();

        foreach (var nopEntityType in new[] { NopEntityType.Product, NopEntityType.ProductAttributeCombination })
        {
            if (!string.IsNullOrWhiteSpace(uuid) &&
                await entityMappingService.GetMappedNopEntityIdByAkeneoUuidAsync(
                    AkeneoEntityType.Product, uuid, nopEntityType) != null)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(identifier) &&
                await entityMappingService.GetMappedNopEntityIdByAkeneoCodeAsync(
                    AkeneoEntityType.Product, identifier, nopEntityType) != null)
            {
                return true;
            }
        }

        if (string.IsNullOrWhiteSpace(identifier))
            return false;

        return await productService.GetProductBySkuAsync(identifier) != null ||
               await productAttributeService.GetProductAttributeCombinationBySkuAsync(identifier) != null;
    }

    private async Task<bool> ProductModelExistsAsync(string code) =>
        await entityMappingService.GetMappedNopEntityIdByAkeneoCodeAsync(
            AkeneoEntityType.ProductModel, code, NopEntityType.Product) != null ||
        await productService.GetProductBySkuAsync(code) != null;

    private async Task<bool> HasSkuMappingAsync(string familyCode)
    {
        var key = familyCode?.Trim() ?? string.Empty;

        if (_familyHasSkuMapping.TryGetValue(key, out var cached))
            return cached;

        var mappings = await attributeMappingService.GetEffectiveMappingsAsync(
            string.IsNullOrWhiteSpace(key) ? null : key);

        // Mapping the identifier attribute itself to Sku (the default) yields the
        // identifier the gate already checks, so only other Sku sources count.
        var hasSkuMapping = mappings.Any(mapping =>
            mapping.NopTargetTypeId == (int)NopTargetType.ProductField &&
            string.Equals(mapping.NopTargetKey?.Trim(), "Sku", StringComparison.OrdinalIgnoreCase) &&
            !(mapping.ValueModeId == (int)AkeneoAttributeMappingValueMode.SingleAttribute &&
              mapping.AkeneoAttributeTypeId == (int)AkeneoAttributeType.Identifier));

        _familyHasSkuMapping[key] = hasSkuMapping;

        return hasSkuMapping;
    }
}
