using Nop.Core.Configuration;

namespace Aperture.Nop.Plugin.Misc.PageCache;

/// <summary>
/// Settings for the Page Cache plugin
/// </summary>
public class PageCacheSettings : ISettings
{
    /// <summary>
    /// Gets or sets cache length for category pages (in minutes)
    /// </summary>
    public int CategoryPageCacheLengthMinutes { get; set; } = 60;

    /// <summary>
    /// Gets or sets cache length for product details pages (in minutes)
    /// </summary>
    public int ProductDetailsPageCacheLengthMinutes { get; set; } = 60;
    public IList<int> PageModifyingCustomerRoleIds { get; set; }
    public bool Enabled { get; set; }
}
