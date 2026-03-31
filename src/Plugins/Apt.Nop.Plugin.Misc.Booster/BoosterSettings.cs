using Nop.Core.Configuration;

namespace Apt.Nop.Plugin.Misc.Booster;

/// <summary>
/// Settings for the Page Cache plugin
/// </summary>
public class BoosterSettings : ISettings
{
    public int CategoryPageCacheLengthMinutes { get; set; }
    public int ProductDetailsPageCacheLengthMinutes { get; set; }
    public int ManufacturerPageCacheLengthMinutes { get; set; }
    public List<int> PageModifyingCustomerRoleIds { get; set; }

    public bool BypassCacheIfProductDiscountsApplied { get; set; }

    public bool Enabled { get; set; }
}