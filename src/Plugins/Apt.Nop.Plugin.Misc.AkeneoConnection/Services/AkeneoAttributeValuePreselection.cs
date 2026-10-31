using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

/// <summary>
/// When none of an attribute's values is preselected, the one with the lowest
/// display order (then the lowest ID) becomes the preselected one. A value
/// already preselected, by hand or earlier, is left as it is. Applied after the
/// sync creates or reorders an attribute's values.
/// </summary>
public static class AkeneoAttributeValuePreselection
{
    public static int? PickPreselected(IEnumerable<(int Id, int DisplayOrder)> values) =>
        values
            .OrderBy(value => value.DisplayOrder)
            .ThenBy(value => value.Id)
            .Select(value => (int?)value.Id)
            .FirstOrDefault();

    /// <summary>True when a value was preselected.</summary>
    public static async Task<bool> ApplyAsync(
        IProductAttributeService productAttributeService,
        int productAttributeMappingId)
    {
        var values = await productAttributeService.GetProductAttributeValuesAsync(productAttributeMappingId);
        if (values.Any(value => value.IsPreSelected))
            return false;

        var preselectedId = PickPreselected(values.Select(value => (value.Id, value.DisplayOrder)));
        var lowest = values.FirstOrDefault(value => value.Id == preselectedId);
        if (lowest == null)
            return false;

        lowest.IsPreSelected = true;
        await productAttributeService.UpdateProductAttributeValueAsync(lowest);
        return true;
    }
}
