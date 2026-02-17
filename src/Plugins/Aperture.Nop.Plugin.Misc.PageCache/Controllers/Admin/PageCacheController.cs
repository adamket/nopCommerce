using System.Threading.Tasks;
using Aperture.Nop.Plugin.Misc.PageCache.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Core;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Web.Areas.Admin.Controllers;
using Nop.Web.Framework.Controllers;

namespace Aperture.Nop.Plugin.Misc.PageCache.Controllers.Admin;

/// <summary>
/// Admin controller for plugin configuration
/// </summary>
public class PageCacheController(
    ISettingService settingService,
    INotificationService notificationService,
    IStoreContext storeContext,
    ILocalizationService localizationService, ICustomerService customerService)
    : BaseAdminController
{
 

    #region Methods

    public async Task<IActionResult> Configure()
    {
        var storeScope = await storeContext.GetActiveStoreScopeConfigurationAsync();
        var settings = await settingService.LoadSettingAsync<PageCacheSettings>(storeScope);
        var customerRoles = await customerService.GetAllCustomerRolesAsync(true);

        var model = new ConfigurationModel
        {
           CategoryPageCacheLengthMinutes = settings.CategoryPageCacheLengthMinutes,
           ProductDetailsPageCacheLengthMinutes = settings.ProductDetailsPageCacheLengthMinutes,
           Enabled = settings.Enabled,
           AvailableCustomerRoles = customerRoles.Select(q=> new SelectListItem(q.Name,q.Id.ToString())).ToList(),
           PageModifyingCustomerRoleIds = settings.PageModifyingCustomerRoleIds,
           ActiveStoreScopeConfiguration = storeScope
        };

        if (storeScope > 0)
        {
            model.CategoryPageCacheLengthMinutes_OverrideForStore = await settingService.SettingExistsAsync(settings, x => x.CategoryPageCacheLengthMinutes, storeScope);
            model.ProductDetailsPageCacheLengthMinutes_OverrideForStore = await settingService.SettingExistsAsync(settings, x => x.ProductDetailsPageCacheLengthMinutes, storeScope);
            model.Enabled_OverrideForStore = await settingService.SettingExistsAsync(settings, x => x.Enabled, storeScope);
        }

        return View("~/Plugins/Aperture.Misc.PageCache/Views/Configure.cshtml", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Configure(ConfigurationModel model)
    {
        if (!ModelState.IsValid)
            return await Configure();

        //load settings for a chosen store scope
        var storeScope = await storeContext.GetActiveStoreScopeConfigurationAsync();
        var settings = await settingService.LoadSettingAsync<PageCacheSettings>(storeScope);

        //save settings
        settings.CategoryPageCacheLengthMinutes = model.CategoryPageCacheLengthMinutes;
        settings.ProductDetailsPageCacheLengthMinutes = model.ProductDetailsPageCacheLengthMinutes;
        settings.Enabled = model.Enabled;
        settings.PageModifyingCustomerRoleIds = model.PageModifyingCustomerRoleIds.ToList();

        /* We do not clear cache after each setting update.
         * This behavior can increase performance because cached settings will not be cleared
         * and loaded from database after each update */

        await settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.CategoryPageCacheLengthMinutes, model.CategoryPageCacheLengthMinutes_OverrideForStore, storeScope, false);
        await settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.ProductDetailsPageCacheLengthMinutes, model.ProductDetailsPageCacheLengthMinutes_OverrideForStore, storeScope, false);
        await settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.Enabled, model.Enabled_OverrideForStore, storeScope, false);
        await settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.PageModifyingCustomerRoleIds, model.PageModifyingCustomerRoleIds_OverrideForStore, storeScope, false);

        //now clear settings cache
        await settingService.ClearCacheAsync();

        notificationService.SuccessNotification(await localizationService.GetResourceAsync("Admin.Plugins.Saved"));

        return await Configure();
    }

    #endregion
}
