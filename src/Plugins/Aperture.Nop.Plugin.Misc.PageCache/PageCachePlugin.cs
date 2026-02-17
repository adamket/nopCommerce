using System.Threading.Tasks;
using Nop.Core;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Plugins;

namespace Aperture.Nop.Plugin.Misc.PageCache;

/// <summary>
/// Represents the Page Cache plugin
/// </summary>
public class PageCachePlugin : BasePlugin, IMiscPlugin
{
    #region Fields

    private readonly IWebHelper _webHelper;
    private readonly ISettingService _settingService;
    private readonly ILocalizationService _localizationService;

    #endregion

    #region Ctor

    public PageCachePlugin(IWebHelper webHelper, ISettingService settingService, ILocalizationService localizationService)
    {
        _webHelper = webHelper;
        _settingService = settingService;
        _localizationService = localizationService;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Gets a configuration page URL
    /// </summary>
    public override string GetConfigurationPageUrl()
    {
        return $"{_webHelper.GetStoreLocation()}admin/apt/page-cache/configure";
    }

    /// <summary>
    /// Install the plugin
    /// </summary>
    public override async Task InstallAsync()
    {

        //settings
        var settings = new PageCacheSettings
        {
            CategoryPageCacheLengthMinutes = 60,
            ProductDetailsPageCacheLengthMinutes = 60
        };
        await _settingService.SaveSettingAsync(settings);

        //locales
        await _localizationService.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>
        {
            ["Plugins.Misc.PageCache.Instructions"] =
                "Page Cache improves storefront performance by storing fully cached pages for a configurable duration. Cached pages are served instantly to customers, reducing server load and improving response times.",

            ["Aperture.Plugins.Misc.PageCache.Fields.CategoryPageCacheLengthMinutes"] =
                "Category page cache duration (minutes)",

            ["Aperture.Plugins.Misc.PageCache.Fields.CategoryPageCacheLengthMinutes.Hint"] =
                "Number of minutes category pages should remain cached before being regenerated.",

            ["Aperture.Plugins.Misc.PageCache.Fields.ProductDetailsPageCacheLengthMinutes"] =
                "Product page cache duration (minutes)",

            ["Aperture.Plugins.Misc.PageCache.Fields.ProductDetailsPageCacheLengthMinutes.Hint"] =
                "Number of minutes product detail pages should remain cached before being regenerated.",

            ["Aperture.Plugins.Misc.PageCache.Fields.Enabled"] =
                "Enabled",

            ["Aperture.Plugins.Misc.PageCache.Fields.Enabled.Hint"] =
                "When enabled, eligible storefront pages will be served from cache for faster performance.",

            ["Aperture.Plugins.Misc.PageCache.Fields.PageModifyingCustomerRoleIds"] =
                "Separate cache by customer roles",

            ["Aperture.Plugins.Misc.PageCache.Fields.PageModifyingCustomerRoleIds.Hint"] =
                "Pages will be cached separately for customers in these roles."
        });

        await base.InstallAsync();

    }

    /// <summary>
    /// Uninstall the plugin
    /// </summary>
    public override async Task UninstallAsync()
    {
        await base.UninstallAsync();
    }

    #endregion
}
