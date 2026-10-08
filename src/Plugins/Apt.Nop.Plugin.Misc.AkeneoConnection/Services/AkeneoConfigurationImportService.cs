using System.Transactions;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Nop.Core.Caching;
using Nop.Core.Domain.Catalog;
using Nop.Data;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public interface IAkeneoConfigurationImportService
{
    Task ImportAsync(string json, CancellationToken cancellationToken = default);
}

public sealed class AkeneoConfigurationImportService(
    IRepository<AkeneoFamilyMapping> families,
    IRepository<AkeneoFamilyVariantAxisMapping> axes,
    IRepository<AkeneoFamilySubModelRule> rules,
    IRepository<AkeneoAttributeMapping> attributes,
    IRepository<AkeneoAttributeMappingFallbackSource> fallbacks,
    IRepository<AkeneoAssetMapping> assets,
    IRepository<AkeneoNopEntityMapping> entities,
    IAkeneoSyncLeaseService leases,
    IStaticCacheManager cache,
    IAkeneoValueTemplateRenderer templates,
    IRepository<ProductAttribute> productAttributes,
    IRepository<SpecificationAttribute> specificationAttributes,
    IRepository<Category> categories,
    IRepository<Manufacturer> manufacturers,
    IAkeneoTargetTypeResolver targetTypes)
    : IAkeneoConfigurationImportService
{
    private async Task ValidateDestinationsAsync(AkeneoConfigurationImportDocument document)
    {
        foreach (var section in document.FamilyMappings)
        {
            if (section.Configuration.AssociatedProductAttributeId is > 0)
                await RequireEntityAsync(productAttributes, section.Configuration.AssociatedProductAttributeId.Value);
            foreach (var axis in section.AxisMappings)
                await RequireEntityAsync(productAttributes, axis.NopProductAttributeId);
        }
        foreach (var section in document.AttributeMappings)
        {
            var mapping = section.Configuration;
            switch ((NopTargetType)mapping.NopTargetTypeId)
            {
                case NopTargetType.ProductAttribute:
                    await RequireEntityAsync(productAttributes, mapping.NopTargetEntityId ?? 0); break;
                case NopTargetType.SpecificationAttribute:
                    await RequireEntityAsync(specificationAttributes, mapping.NopTargetEntityId ?? 0); break;
                case NopTargetType.Category:
                    if (mapping.NopTargetEntityId is > 0) await RequireEntityAsync(categories, mapping.NopTargetEntityId.Value);
                    break;
                case NopTargetType.Manufacturer:
                    if (mapping.NopTargetEntityId is > 0) await RequireEntityAsync(manufacturers, mapping.NopTargetEntityId.Value);
                    break;
                case NopTargetType.ProductField:
                case NopTargetType.SeoField:
                    AkeneoConfigurationImportDocument.Require(targetTypes.GetTargetKeyOptions((NopTargetType)mapping.NopTargetTypeId)
                        .Any(k => string.Equals(k.Code, mapping.NopTargetKey, StringComparison.OrdinalIgnoreCase)),
                        "Unknown destination field: " + mapping.NopTargetKey);
                    break;
            }
        }
        foreach (var category in document.CategoryMappings)
            await RequireEntityAsync(categories, category.NopEntityId);
    }

    private static async Task RequireEntityAsync<T>(IRepository<T> repository, int id) where T : global::Nop.Core.BaseEntity
    {
        AkeneoConfigurationImportDocument.Require(id > 0 && await repository.GetByIdAsync(id, includeDeleted: false) != null,
            typeof(T).Name + " destination ID " + id + " does not exist in this store.");
    }
    public async Task ImportAsync(string json, CancellationToken cancellationToken = default)
    {
        var document = AkeneoConfigurationImportDocument.Parse(json);
        foreach (var section in document.AttributeMappings.Where(s => s.Configuration.ValueMode == AkeneoAttributeMappingValueMode.Template))
        {
            var validation = templates.Validate(section.Configuration.ValueTemplate);
            AkeneoConfigurationImportDocument.Require(validation.Success,
                "Invalid template in mapping '" + section.Configuration.Name + "'.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        await ValidateDestinationsAsync(document);
        var lease = await leases.TryAcquireAsync(AkeneoConnectionConstants.CatalogWriterLockKey, TimeSpan.FromMinutes(10));
        if (lease == null) throw new InvalidOperationException("A catalog sync or configuration import is running. Try again after it finishes.");
        try
        {
            // Match the plugin's existing ambient transaction pattern. Deletions
            // and inserts roll back together, including on request cancellation.
            using (var transaction = new TransactionScope(TransactionScopeOption.Required,
                       new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted, Timeout = TimeSpan.FromMinutes(5) },
                       TransactionScopeAsyncFlowOption.Enabled))
            {
                await fallbacks.DeleteAsync(_ => true);
                await axes.DeleteAsync(_ => true);
                await rules.DeleteAsync(_ => true);
                await attributes.DeleteAsync(_ => true);
                await families.DeleteAsync(_ => true);
                await assets.DeleteAsync(_ => true);
                await entities.DeleteAsync(e => e.AkeneoEntityTypeId == (int)AkeneoEntityType.Category && e.NopEntityTypeId == (int)NopEntityType.Category);
                foreach (var section in document.FamilyMappings)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    section.Configuration.Id = 0;
                    await families.InsertAsync(section.Configuration, false);
                    foreach (var axis in section.AxisMappings)
                    {
                        axis.Id = 0;
                        axis.FamilyVariantImportConfigurationId = section.Configuration.Id;
                        await axes.InsertAsync(axis, false);
                    }
                    foreach (var rule in section.SubModelRules)
                    {
                        rule.Id = 0;
                        rule.FamilyMappingId = section.Configuration.Id;
                        await rules.InsertAsync(rule, false);
                    }
                }
                foreach (var section in document.AttributeMappings)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    section.Configuration.Id = 0;
                    await attributes.InsertAsync(section.Configuration, false);
                    foreach (var fallback in section.FallbackSources)
                    {
                        fallback.Id = 0;
                        fallback.AttributeMappingId = section.Configuration.Id;
                        await fallbacks.InsertAsync(fallback, false);
                    }
                }
                foreach (var asset in document.AssetMappings)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    asset.Id = 0;
                    await assets.InsertAsync(asset, false);
                }
                foreach (var category in document.CategoryMappings)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    category.Id = 0;
                    await entities.InsertAsync(category, false);
                }
                cancellationToken.ThrowIfCancellationRequested();
                transaction.Complete();
            }
        }
        finally
        {
            try
            {
                await cache.RemoveByPrefixAsync(AkeneoConnectionConstants.AttributeMappingPrefix);
                await cache.RemoveByPrefixAsync(AkeneoConnectionConstants.AssetMappingPrefix);
                await cache.RemoveByPrefixAsync(AkeneoConnectionConstants.EntityMappingAllPrefix);
            }
            finally { await leases.ReleaseAsync(lease); }
        }
    }
}
