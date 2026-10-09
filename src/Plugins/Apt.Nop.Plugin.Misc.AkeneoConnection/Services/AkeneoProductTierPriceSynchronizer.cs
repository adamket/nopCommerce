using System.Globalization;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services.Planning;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using static Apt.Nop.Plugin.Misc.AkeneoConnection.Services.DryRun.AkeneoDryRunPlanHelper;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

/// <summary>
/// Writes "Tier price" attribute mappings as nopCommerce tier prices: one
/// tier price per mapping for a customer role and minimum quantity, in all
/// stores, with no start or end date. The mapping stores the customer role in
/// NopTargetEntityId and the minimum quantity in NopTargetKey (default 1).
/// Only tier prices this plugin wrote (tracked as managed relations) are ever
/// removed; manually created tier prices are never deleted.
/// </summary>
public class AkeneoProductTierPriceSynchronizer(
    IProductService productService,
    ICustomerService customerService,
    IAkeneoManagedRelationService managedRelationService)
    : IAkeneoProductSectionSynchronizer,
      IAkeneoSectionDryRunPlanProvider
{
    private const string Area = "Tier prices";

    public const int DefaultMinimumQuantity = 1;

    public int Order => 650;

    public async Task SynchronizeAsync(
        AkeneoProductSyncContext context,
        CancellationToken cancellationToken = default)
    {
        if (context.Product is null)
            return;

        var plan = await BuildPlanAsync(context, cancellationToken);
        await plan.ExecuteAsync(context, cancellationToken);
    }

    public async Task PlanAsync(
        AkeneoProductSyncContext context,
        AkeneoProductMappingPreviewModel model,
        CancellationToken cancellationToken = default)
    {
        var plan = await BuildPlanAsync(context, cancellationToken);
        plan.Render(model);
    }

    /// <summary>
    /// The minimum quantity stored on a tier-price mapping (its target key).
    /// </summary>
    public static int GetMinimumQuantity(AkeneoAttributeMapping mapping) =>
        int.TryParse(mapping?.NopTargetKey?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantity) &&
        quantity >= 1
            ? quantity
            : DefaultMinimumQuantity;

    private async Task<AkeneoExecutableSectionPlan> BuildPlanAsync(
        AkeneoProductSyncContext context,
        CancellationToken cancellationToken)
    {
        var plan = new AkeneoExecutableSectionPlan();
        var mappings = context.GetMappings(NopTargetType.TierPrice).ToList();

        if (mappings.Count == 0)
            return plan;

        var existingTierPrices = context.ExistingProduct == null
            ? new List<TierPrice>()
            : await productService.GetTierPricesByProductAsync(context.ExistingProduct.Id);

        var syncProfileId = context.Request.SyncProfileId;

        foreach (var mapped in mappings)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var roleId = mapped.Mapping.NopTargetEntityId ?? 0;
            var quantity = GetMinimumQuantity(mapped.Mapping);
            var role = roleId > 0
                ? await customerService.GetCustomerRoleByIdAsync(roleId)
                : null;

            if (role == null)
            {
                plan.AddReview(
                    Area,
                    $"Customer role #{roleId}",
                    GetSourceName(mapped),
                    "The tier-price mapping's customer role no longer exists in nopCommerce. Edit the mapping and pick a role.",
                    mapped.Mapping.IsRequired);
                continue;
            }

            var target = $"{role.Name} · minimum quantity {quantity}";

            // The slot this mapping owns: role + quantity, all stores, no dates.
            var existing = existingTierPrices.FirstOrDefault(tierPrice =>
                tierPrice.CustomerRoleId == roleId &&
                tierPrice.Quantity == quantity &&
                tierPrice.StoreId == 0 &&
                tierPrice.StartDateTimeUtc == null &&
                tierPrice.EndDateTimeUtc == null);

            var current = existing?.Price.ToString("0.####", CultureInfo.InvariantCulture);

            if (!mapped.HasValue)
            {
                await PlanMissingValueAsync(plan, context, mapped, target, existing, current, syncProfileId);
                continue;
            }

            if (!decimal.TryParse(
                    mapped.DisplayValue,
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out var price) ||
                price < 0)
            {
                plan.AddReview(
                    Area,
                    target,
                    GetSourceName(mapped),
                    $"The value '{mapped.DisplayValue}' is not a valid price, so the tier price was left unchanged.",
                    mapped.Mapping.IsRequired,
                    current,
                    mapped.DisplayValue);
                continue;
            }

            var proposed = price.ToString("0.####", CultureInfo.InvariantCulture);

            if (existing != null && existing.Price == price)
            {
                plan.AddOperation(
                    Area,
                    target,
                    GetSourceName(mapped),
                    current,
                    proposed,
                    AkeneoDryRunOperationType.NoChange,
                    isRequired: mapped.Mapping.IsRequired);

                // The mapping now owns this slot, even if the row was created
                // elsewhere, so a later empty value can remove it.
                plan.AddInternalOperation(async _ =>
                {
                    await ClaimAsync(context, syncProfileId, existing.Id, mapped.Mapping.AkeneoAttributeCode);
                    return false;
                });

                continue;
            }

            if (existing != null)
            {
                var tierPrice = existing;

                plan.AddOperation(
                    Area,
                    target,
                    GetSourceName(mapped),
                    current,
                    proposed,
                    AkeneoDryRunOperationType.Update,
                    isRequired: mapped.Mapping.IsRequired,
                    executeAsync: async _ =>
                    {
                        tierPrice.Price = price;
                        await productService.UpdateTierPriceAsync(tierPrice);
                        await ClaimAsync(context, syncProfileId, tierPrice.Id, mapped.Mapping.AkeneoAttributeCode);

                        context.Result.AddMessage(
                            $"Updated the {role.Name} tier price (minimum quantity {quantity}) to {proposed}.");
                        return true;
                    });

                continue;
            }

            plan.AddOperation(
                Area,
                target,
                GetSourceName(mapped),
                null,
                proposed,
                AkeneoDryRunOperationType.Add,
                isRequired: mapped.Mapping.IsRequired,
                executeAsync: async _ =>
                {
                    if (context.Product == null)
                        return false;

                    var tierPrice = new TierPrice
                    {
                        ProductId = context.Product.Id,
                        StoreId = 0,
                        CustomerRoleId = roleId,
                        Quantity = quantity,
                        Price = price
                    };

                    await productService.InsertTierPriceAsync(tierPrice);
                    await ClaimAsync(context, syncProfileId, tierPrice.Id, mapped.Mapping.AkeneoAttributeCode);

                    context.Result.AddMessage(
                        $"Added a {role.Name} tier price (minimum quantity {quantity}) of {proposed}.");
                    return true;
                });
        }

        return plan;
    }

    private async Task PlanMissingValueAsync(
        AkeneoExecutableSectionPlan plan,
        AkeneoProductSyncContext context,
        AkeneoResolvedMappedValue mapped,
        string target,
        TierPrice existing,
        string current,
        int? syncProfileId)
    {
        if (existing == null)
        {
            plan.AddOperation(
                Area,
                target,
                GetSourceName(mapped),
                null,
                null,
                AkeneoDryRunOperationType.NoChange,
                "The mapping produced no value and no tier price exists.",
                mapped.Mapping.IsRequired);
            return;
        }

        if (context.Request.ProductFieldMissingValueBehavior ==
            AkeneoMissingValueBehavior.PreserveExisting)
        {
            plan.AddOperation(
                Area,
                target,
                GetSourceName(mapped),
                current,
                current,
                AkeneoDryRunOperationType.Preserve,
                "The mapping produced no value and the profile preserves existing product-field values.",
                mapped.Mapping.IsRequired);
            return;
        }

        var owned = syncProfileId.HasValue &&
                    await managedRelationService.GetByRelationEntityAsync(
                        syncProfileId.Value,
                        AkeneoManagedRelationType.TierPrice,
                        existing.Id) != null;

        if (!owned)
        {
            plan.AddOperation(
                Area,
                target,
                GetSourceName(mapped),
                current,
                current,
                AkeneoDryRunOperationType.Preserve,
                "The mapping produced no value, but this tier price was not written by the plugin, so it is left in place.",
                mapped.Mapping.IsRequired);
            return;
        }

        plan.AddOperation(
            Area,
            target,
            GetSourceName(mapped),
            current,
            null,
            AkeneoDryRunOperationType.Remove,
            "The mapping produced no value and the profile clears missing product-field values.",
            mapped.Mapping.IsRequired,
            async _ =>
            {
                var relation = await managedRelationService.GetByRelationEntityAsync(
                    syncProfileId.Value,
                    AkeneoManagedRelationType.TierPrice,
                    existing.Id);

                await productService.DeleteTierPriceAsync(existing);

                if (relation != null)
                    await managedRelationService.DeleteAsync(relation);

                context.Result.AddMessage($"Removed the {target} tier price.");
                return true;
            });
    }

    private async Task ClaimAsync(
        AkeneoProductSyncContext context,
        int? syncProfileId,
        int tierPriceId,
        string akeneoAttributeCode)
    {
        if (!syncProfileId.HasValue || context.Product == null)
            return;

        await managedRelationService.UpsertAsync(
            syncProfileId.Value,
            context.Request.SyncRunRecordId,
            context.Product.Id,
            AkeneoManagedRelationType.TierPrice,
            tierPriceId,
            akeneoAttributeCode);
    }
}
