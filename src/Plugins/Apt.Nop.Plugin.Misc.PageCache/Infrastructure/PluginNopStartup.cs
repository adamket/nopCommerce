using Apt.Nop.Plugin.Misc.PageCache.Filters;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Infrastructure;

namespace Apt.Nop.Plugin.Misc.PageCache.Infrastructure;

/// <summary>
/// Represents the object for configuring services on application startup
/// </summary>
public class PluginNopStartup : INopStartup
{
    /// <summary>
    /// Add and configure any of the middleware
    /// </summary>
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MvcOptions>(options =>
        {
            options.Filters.Add<ProductDetailsActionAttribute>();
            options.Filters.Add<CategoryActionAttribute>();
        });
    }

    /// <summary>
    /// Configure the using of added middleware
    /// </summary>
    public void Configure(IApplicationBuilder application)
    {
    }

    /// <summary>
    /// Gets order of this startup configuration implementation
    /// </summary>
    public int Order => 3000;
}
