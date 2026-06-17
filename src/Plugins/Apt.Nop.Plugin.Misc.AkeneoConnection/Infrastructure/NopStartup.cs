using Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
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
        services.AddScoped<IAkeneoAttributeMappingModelFactory, AkeneoAttributeAttributeMappingModelFactory>();
        services.AddScoped<IAkeneoNopEntityMappingService, AkeneoNopEntityMappingService>();
        services.AddScoped<IAkeneoAttributeMappingService, AkeneoAttributeMappingService>();
        services.AddScoped<IAkeneoSyncProfileService, AkeneoSyncProfileService>();
        services.AddScoped<IAkeneoSyncItemLogService, AkeneoSyncItemLogService>();
        services.AddScoped<IAkeneoProductValueResolver, AkeneoProductValueResolver>();
        services.AddScoped<IAkeneoProductMappingFactory, AkeneoProductMappingFactory>();
        services.AddScoped<IAkeneoCategoryMappingModelFactory, AkeneoCategoryMappingModelFactory>();

        services.AddSingleton<IAkeneoTargetTypeResolver, AkeneoTargetTypeResolver>();
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