using System.Xml.Linq;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

/// <summary>
/// Deletes a managed combination and then removes plugin-owned axis values only
/// when no remaining combination references them.
/// </summary>
public class AkeneoManagedVariantCleanupService(
    IProductAttributeService productAttributeService,
    IAkeneoManagedRelationService managedRelationService)
    : IAkeneoManagedVariantCleanupService
{
    public async Task DeleteCombinationAndUnusedAxisValuesAsync(
        int syncProfileId,
        ProductAttributeCombination combination,
        CancellationToken cancellationToken = default)
    {
        if (combination == null)
            return;

        cancellationToken.ThrowIfCancellationRequested();

        var selectedValueIds = GetSelectedValueIds(combination.AttributesXml);
        var productId = combination.ProductId;

        await productAttributeService.DeleteProductAttributeCombinationAsync(combination);

        var combinationRelation = await managedRelationService.GetByRelationEntityAsync(
            syncProfileId,
            AkeneoManagedRelationType.ProductAttributeCombination,
            combination.Id);
        await managedRelationService.DeleteAsync(combinationRelation);

        if (selectedValueIds.Count == 0)
            return;

        var remainingCombinations = await productAttributeService
            .GetAllProductAttributeCombinationsAsync(productId);

        var remainingValueIds = remainingCombinations
            .SelectMany(item => GetSelectedValueIds(item.AttributesXml))
            .ToHashSet();

        foreach (var valueId in selectedValueIds.Where(id =>
                     !remainingValueIds.Contains(id)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relation = await managedRelationService.GetByRelationEntityAsync(
                syncProfileId,
                AkeneoManagedRelationType.ProductAttributeValue,
                valueId);

            if (relation == null)
                continue;

            var value = await productAttributeService
                .GetProductAttributeValueByIdAsync(valueId);

            if (value != null)
                await productAttributeService.DeleteProductAttributeValueAsync(value);

            await managedRelationService.DeleteAsync(relation);
        }
    }

    private static HashSet<int> GetSelectedValueIds(string attributesXml)
    {
        if (string.IsNullOrWhiteSpace(attributesXml))
            return new HashSet<int>();

        try
        {
            return XDocument.Parse(attributesXml)
                .Descendants("Value")
                .Select(element => int.TryParse(element.Value, out var id) ? id : 0)
                .Where(id => id > 0)
                .ToHashSet();
        }
        catch
        {
            return new HashSet<int>();
        }
    }
}
