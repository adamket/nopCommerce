using System.Net;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Infrastructure;


namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Infrastructure;

/// <summary>
/// Represents object for the configuring services on application startup
/// </summary>
public class NopStartup : INopStartup
{
    /// <summary>
    /// Add and configure any of the middleware
    /// </summary>
    /// <param name="services">Collection of service descriptors</param>
    /// <param name="configuration">Configuration of the application</param>
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        //override services
        services.AddScoped<IAkeneoApiClient, AkeneoApiClient>();
        services.AddScoped<IAkeneoConfigurationExportService, AkeneoConfigurationExportService>();
        services.AddScoped<IAkeneoAttributeMappingModelFactory, AkeneoAttributeMappingModelFactory>();
        services.AddScoped<IAkeneoNopEntityMappingService, AkeneoNopEntityMappingService>();
        services.AddScoped<IAkeneoAttributeMappingService, AkeneoAttributeMappingService>();
        services.AddScoped<IAkeneoSyncProfileService, AkeneoSyncProfileService>();
        services.AddScoped<IAkeneoSyncItemLogService, AkeneoSyncItemLogService>();
        services.AddScoped<IAkeneoProductValueResolver, AkeneoProductValueResolver>();
        services.AddScoped<IAkeneoReferenceEntityValueResolver, AkeneoReferenceEntityValueResolver>();
        services.AddScoped<IAkeneoValueTransformationService, AkeneoValueTransformationService>();
        services.AddScoped<IAkeneoValueTemplateRenderer, AkeneoValueTemplateRenderer>();
        services.AddScoped<IAkeneoAssetMappingService, AkeneoAssetMappingService>();
        services.AddScoped<IAkeneoManagedAssetService, AkeneoManagedAssetService>();
        services.AddScoped<IAkeneoAssetMappingModelFactory, AkeneoAssetMappingModelFactory>();
        services.AddScoped<IAkeneoAssetResolver, AkeneoAssetResolver>();
        services.AddScoped<IAkeneoExternalAssetDownloader, AkeneoExternalAssetDownloader>();
        services.AddScoped<IAkeneoProductMappingFactory, AkeneoProductMappingFactory>();
        services.AddScoped<IAkeneoCategoryMappingModelFactory, AkeneoCategoryMappingModelFactory>();
        services.AddScoped<IAkeneoProductBatchSyncService, AkeneoProductBatchSyncService>();
        services.AddScoped<
            IAkeneoProductModelDeltaFanOutService,
            AkeneoProductModelDeltaFanOutService>();
        services.AddScoped<IAkeneoSyncRunRecordService, AkeneoSyncRunRecordService>();
        services.AddScoped<IAkeneoSyncProfileModelFactory, AkeneoSyncProfileModelFactory>();
        services.AddScoped<IAkeneoProductBatchImportRequestFactory, AkeneoProductBatchImportRequestFactory>();
        services.AddScoped<IAkeneoProductSyncExecutionService, AkeneoProductSyncExecutionService>();
        services.AddScoped<IAkeneoVariantRelationshipService, AkeneoVariantRelationshipService>();
        services.AddScoped<IAkeneoVariantRelationshipResolver, AkeneoVariantRelationshipResolver>();
        services.AddScoped<INopVariantStructureDetector, NopVariantStructureDetector>();
        services.AddScoped<IAkeneoProductSyncStateService, AkeneoProductSyncStateService>();
        services.AddScoped<IAkeneoManagedRelationService, AkeneoManagedRelationService>();
        services.AddScoped<IAkeneoSyncLeaseService, AkeneoSyncLeaseService>();
        services.AddScoped<IAkeneoCatalogReconciliationService, AkeneoCatalogReconciliationService>();
        services.AddScoped<IAkeneoVariantRepresentationCleanupService, AkeneoVariantRepresentationCleanupService>();
        services.AddScoped<IAkeneoManagedVariantCleanupService, AkeneoManagedVariantCleanupService>();
        services.AddScoped<
            IAkeneoProductModelHierarchyResolver,
            AkeneoProductModelHierarchyResolver>();

        services.AddScoped<
            IAkeneoProductSyncService,
            AkeneoProductSyncService>();

        services.AddScoped<
            IAkeneoProductSyncPipeline,
            AkeneoProductSyncPipeline>();

        services.AddScoped<
            IAkeneoProductSectionSynchronizer,
            AkeneoProductCoreSynchronizer>();

        services.AddScoped<
            IAkeneoProductSectionSynchronizer,
            AkeneoProductCategorySynchronizer>();

        services.AddScoped<
            IAkeneoFamilyMappingService,
            AkeneoFamilyMappingService>();

        services.AddScoped<
            IAkeneoFamilyMappingModelFactory,
            AkeneoFamilyMappingModelFactory>();


        services.AddScoped<
            IAkeneoProductSectionSynchronizer,
            AkeneoProductSeoSynchronizer>();

        services.AddScoped<
            IAkeneoProductSectionSynchronizer,
            AkeneoProductSpecificationSynchronizer>();

        services.AddScoped<
            IAkeneoProductSectionSynchronizer,
            AkeneoProductAttributeSynchronizer>();

        services.AddScoped<
            IAkeneoProductSectionSynchronizer,
            AkeneoProductAssetSynchronizer>();

        services.AddScoped<
            IAkeneoProductSectionSynchronizer,
            AkeneoProductCustomPropertySynchronizer>();

        services.AddScoped<AkeneoProductSearchJsonBuilder>();
        services.AddSingleton<IAkeneoTargetTypeResolver, AkeneoTargetTypeResolver>();

        services.AddTransient<AkeneoTransientRetryHandler>();

        services.AddHttpClient(AkeneoConnectionConstants.SystemName, client =>
            {
                // Generous overall ceiling; the retry handler applies a shorter PER-ATTEMPT
                // timeout so a single hung request can't consume the whole budget.
                client.Timeout = TimeSpan.FromSeconds(100);
                client.DefaultRequestHeaders.Accept.Add(
                    new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Apt-NopCommerce-AkeneoConnection/1.0");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                MaxConnectionsPerServer = 20
            })
            .SetHandlerLifetime(TimeSpan.FromMinutes(5))
            .AddHttpMessageHandler<AkeneoTransientRetryHandler>();

        services.AddHttpClient(AkeneoConnectionConstants.ExternalAssetHttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(60);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Apt-NopCommerce-AkeneoAssetImporter/1.0");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                MaxConnectionsPerServer = 8
            })
            .SetHandlerLifetime(TimeSpan.FromMinutes(5));
    }

    /// <summary>
    /// Configure the using of added middleware
    /// </summary>
    /// <param name="application">Builder for configuring an application's request pipeline</param>
    public void Configure(IApplicationBuilder application)
    {
    }

    /// <summary>
    /// Gets order of this startup configuration implementation
    /// </summary>
    public int Order => 3000;
}