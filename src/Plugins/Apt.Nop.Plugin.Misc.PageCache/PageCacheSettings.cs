using Nop.Core.Configuration;

namespace Apt.Nop.Plugin.Misc.PageCache;

/// <summary>
/// Settings for the Page Cache plugin
/// </summary>
public class PageCacheSettings : ISettings
{
    /// <summary>
    /// Gets or sets cache length for category pages (in minutes)
    /// </summary>
    public int CategoryPageCacheLengthMinutes { get; set; }

    /// <summary>
    /// Gets or sets cache length for product details pages (in minutes)
    /// </summary>
    public int ProductDetailsPageCacheLengthMinutes { get; set; }
    public List<int> PageModifyingCustomerRoleIds { get; set; }

    public bool BypassCacheIfProductDiscountsApplied { get; set; }

    public bool Enabled { get; set; }
}
