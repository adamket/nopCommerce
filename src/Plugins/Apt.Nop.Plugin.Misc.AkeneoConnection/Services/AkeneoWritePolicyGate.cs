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
                await IsLiveTargetAsync(nopEntityType,
                    await entityMappingService.GetMappedNopEntityIdByAkeneoUuidAsync(
                        AkeneoEntityType.Product, uuid, nopEntityType)))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(identifier) &&
                await IsLiveTargetAsync(nopEntityType,
                    await entityMappingService.GetMappedNopEntityIdByAkeneoCodeAsync(
                        AkeneoEntityType.Product, identifier, nopEntityType)))
            {
                return true;
            }
        }

        if (string.IsNullOrWhiteSpace(identifier))
            return false;

        // GetProductBySkuAsync already excludes deleted products.
        if (await productService.GetProductBySkuAsync(identifier) != null)
            return true;

        var combination = await productAttributeService
            .GetProductAttributeCombinationBySkuAsync(identifier);

        return combination != null &&
               await IsLiveProductAsync(combination.ProductId);
    }

    private async Task<bool> ProductModelExistsAsync(string code) =>
        await IsLiveProductAsync(
            await entityMappingService.GetMappedNopEntityIdByAkeneoCodeAsync(
                AkeneoEntityType.ProductModel, code, NopEntityType.Product)) ||
        await productService.GetProductBySkuAsync(code) != null;

    // Deleted products are never sync destinations, so bindings to them (or
    // to combinations of a deleted parent) do not count as a match.
    private async Task<bool> IsLiveTargetAsync(NopEntityType nopEntityType, int? entityId)
    {
        if (!entityId.HasValue || entityId.Value <= 0)
            return false;

        if (nopEntityType == NopEntityType.ProductAttributeCombination)
        {
            var combination = await productAttributeService
                .GetProductAttributeCombinationByIdAsync(entityId.Value);

            return combination != null &&
                   await IsLiveProductAsync(combination.ProductId);
        }

        return await IsLiveProductAsync(entityId);
    }

    private async Task<bool> IsLiveProductAsync(int? productId)
    {
        if (!productId.HasValue || productId.Value <= 0)
            return false;

        var product = await productService.GetProductByIdAsync(productId.Value);
        return product is { Deleted: false };
    }

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
