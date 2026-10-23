using System.Globalization;
using System.Text.Json;
using System.Transactions;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services.Planning;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;
namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services.CustomMappings;

/// <summary>
/// Replaces the product's table-shaped tier prices: those for all customers and all stores,
/// with no start or end date, which is the only shape this handler writes. Role-, store- and
/// date-specific tier prices are never touched. New prices use the primary store currency.
/// </summary>
public sealed class TierPriceTableMappingHandler(IProductService products) : IAkeneoCustomMappingHandler
{
    public const string HandlerKey = "tier-price-table";
    public string Key => HandlerKey;
    public string DisplayName => "Tier prices (from table)";
    public IReadOnlyCollection<string> SupportedAkeneoTypes => ["pim_catalog_table"];
    public IEnumerable<string> Validate(AkeneoAttributeMapping mapping, AkeneoAttributeDefinition attribute)
    {
        if (!string.Equals(attribute?.Type, "pim_catalog_table", StringComparison.OrdinalIgnoreCase))
        {
            yield return "Tier prices requires an Akeneo table attribute.";
            yield break;
        }
        foreach (var code in new[] { "min_qty", "tier_price" })
        {
            var columns = attribute.TableConfiguration?.Where(c => c.Code == code).ToList();
            if (columns?.Count != 1 || !string.Equals(columns[0].DataType, "number", StringComparison.OrdinalIgnoreCase))
                yield return "Table attribute '" + attribute.Code + "' must contain exactly one number column '" + code + "'.";
        }
    }

    public async Task PlanAsync(AkeneoCustomMappingContext custom, AkeneoExecutableSectionPlan plan,
        CancellationToken cancellationToken = default)
    {
        var context = custom.ProductContext;
        var mapped = custom.MappedValue;
        var mapping = mapped.Mapping;
        void Review(string message) => plan.AddReview("Tier prices", DisplayName, mapping.AkeneoAttributeCode, message, mapping.IsRequired);
        if (context.GetMappings(NopTargetType.TierPrice).Any())
        {
            Review("The tier-price table handler cannot run alongside scalar Tier Price mappings on the same product.");
            return;
        }
        var raw = mapped.Value?.RawData;
        if (raw == null || raw.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            if (mapping.IsRequired) Review("Required tier-price table is missing; existing prices were preserved.");
            else plan.AddOperation("Tier prices", DisplayName, mapping.AkeneoAttributeCode, null, null,
                AkeneoDryRunOperationType.Preserve, "Source table is missing; existing prices are preserved.");
            return;
        }
        if (!string.IsNullOrEmpty(mapped.Value.SourceAttributeType) &&
            !SupportedAkeneoTypes.Contains(mapped.Value.SourceAttributeType, StringComparer.OrdinalIgnoreCase))
        {
            Review("The product value is not a table attribute; existing prices were preserved.");
            return;
        }
        if (raw.Value.ValueKind != JsonValueKind.Array)
        {
            Review("Tier-price data must be an array of rows; existing prices were preserved.");
            return;
        }
        var desired = new SortedDictionary<int, decimal>();
        var rowIndex = 0;
        foreach (var row in raw.Value.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            rowIndex++;
            if (row.ValueKind != JsonValueKind.Object ||
                !row.TryGetProperty("min_qty", out var qtyCell) || !TryNumber(qtyCell, out var qty) ||
                qty < 1 || qty > int.MaxValue || decimal.Truncate(qty) != qty ||
                !row.TryGetProperty("tier_price", out var priceCell) || !TryNumber(priceCell, out var price) ||
                price < 0 || price > 99999999999999.9999m || decimal.Round(price, 4) != price)
            {
                Review($"Tier-price row {rowIndex} requires a positive whole min_qty and a nonnegative tier_price with at most four decimal places. No tier prices were changed.");
                return;
            }
            if (!desired.TryAdd((int)qty, price))
            {
                Review($"Tier-price row {rowIndex} repeats min_qty {qty}. No tier prices were changed.");
                return;
            }
        }
        if (desired.Count == 0 && mapping.IsRequired)
        {
            Review("Required tier-price table is empty; existing prices were preserved.");
            return;
        }
        var allExisting = context.ExistingProduct == null ? new List<TierPrice>()
            : (await products.GetTierPricesByProductAsync(context.ExistingProduct.Id)).ToList();
        // Only the table's own slot is replaced; every other tier price is left as it is.
        var existing = allExisting.Where(IsTableRow).ToList();
        var untouched = allExisting.Count - existing.Count;
        var wanted = desired.Select(p => new TierPrice { Quantity = p.Key, Price = p.Value }).ToList();
        var current = Describe(existing);
        var proposed = Describe(wanted);
        if (desired.Count == 0 && context.Request.ProductFieldMissingValueBehavior != AkeneoMissingValueBehavior.ClearExisting)
        {
            plan.AddOperation("Tier prices", DisplayName, mapping.AkeneoAttributeCode, current, current,
                AkeneoDryRunOperationType.Preserve, "The table is empty and the profile preserves existing product-field values.");
            return;
        }
        var equal = existing.Count == wanted.Count &&
            existing.Select(p => (p.Quantity, p.Price)).OrderBy(p => p.Quantity).ThenBy(p => p.Price)
                .SequenceEqual(wanted.Select(p => (p.Quantity, p.Price)).OrderBy(p => p.Quantity).ThenBy(p => p.Price));
        plan.AddOperation("Tier prices", DisplayName, mapping.AkeneoAttributeCode, current, proposed,
            equal ? AkeneoDryRunOperationType.NoChange :
                wanted.Count == 0 ? AkeneoDryRunOperationType.Remove :
                existing.Count == 0 ? AkeneoDryRunOperationType.Add : AkeneoDryRunOperationType.Update,
            "Replaces the product's tier prices for all customers and stores, without dates, in the primary store currency." +
                (untouched > 0
                    ? $" {untouched} role-, store- or date-specific tier price{(untouched == 1 ? " is" : "s are")} left unchanged."
                    : string.Empty),
            mapping.IsRequired, equal ? null : async token =>
            {
                if (context.Product == null) return false;
                using var transaction = new TransactionScope(TransactionScopeOption.Required, TransactionScopeAsyncFlowOption.Enabled);
                foreach (var row in existing)
                {
                    token.ThrowIfCancellationRequested();
                    await products.DeleteTierPriceAsync(row);
                }
                foreach (var row in wanted)
                {
                    token.ThrowIfCancellationRequested();
                    await products.InsertTierPriceAsync(new TierPrice
                    {
                        ProductId = context.Product.Id, Quantity = row.Quantity, Price = row.Price,
                        StoreId = 0, CustomerRoleId = null
                    });
                }
                transaction.Complete();
                context.Result.AddMessage("Replaced the product tier-price table from " + mapping.AkeneoAttributeCode + ".");
                return true;
            });
    }
    /// <summary>The slot this handler owns: all customers, all stores, no dates.</summary>
    internal static bool IsTableRow(TierPrice price) =>
        price.StoreId == 0 && (price.CustomerRoleId ?? 0) == 0 &&
        price.StartDateTimeUtc == null && price.EndDateTimeUtc == null;
    private static bool TryNumber(JsonElement cell, out decimal value)
    {
        value = 0;
        return cell.ValueKind is JsonValueKind.Number or JsonValueKind.String &&
            decimal.TryParse(cell.ValueKind == JsonValueKind.String ? cell.GetString() : cell.GetRawText(),
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }
    private static string Describe(IEnumerable<TierPrice> prices) =>
        JsonSerializer.Serialize(prices.OrderBy(p => p.Quantity).ThenBy(p => p.StoreId).ThenBy(p => p.CustomerRoleId)
            .ThenBy(p => p.StartDateTimeUtc).ThenBy(p => p.EndDateTimeUtc).ThenBy(p => p.Price)
            .Select(p => new { p.Quantity, p.Price, p.StoreId, p.CustomerRoleId, p.StartDateTimeUtc, p.EndDateTimeUtc }));
}
