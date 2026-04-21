using Apt.Nop.Plugin.Misc.Booster.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Core;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Security;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Apt.Nop.Plugin.Misc.Booster.Controllers.Admin;


public class BoosterController(
    ISettingService settingService,
    INotificationService notificationService,
    IStoreContext storeContext,
    ILocalizationService localizationService, ICustomerService customerService)
    : BasePluginController
{
    #region Methods

    [HttpGet("admin/plugin/booster/configure")]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public async Task<IActionResult> Configure()
    {
        var storeScope = await storeContext.GetActiveStoreScopeConfigurationAsync();
        var settings = await settingService.LoadSettingAsync<BoosterSettings>(storeScope);
        var customerRoles = await customerService.GetAllCustomerRolesAsync(true);

        var model = new ConfigurationModel
        {
            ManufacturerPageCacheLengthMinutes = settings.ManufacturerPageCacheLengthMinutes,
            CategoryPageCacheLengthMinutes = settings.CategoryPageCacheLengthMinutes,
            ProductDetailsPageCacheLengthMinutes = settings.ProductDetailsPageCacheLengthMinutes,
            
            Enabled = settings.Enabled,
            AvailableCustomerRoles = customerRoles.Select(q => new SelectListItem(q.Name, q.Id.ToString())).ToList(),
            PageModifyingCustomerRoleIds = settings.PageModifyingCustomerRoleIds ?? new List<int>(),
            ActiveStoreScopeConfiguration = storeScope
        };

        if (storeScope > 0)
        {
            model.ManufacturerPageCacheLengthMinutes_OverrideForStore = await settingService.SettingExistsAsync(settings, x => x.ManufacturerPageCacheLengthMinutes, storeScope);
            model.CategoryPageCacheLengthMinutes_OverrideForStore = await settingService.SettingExistsAsync(settings, x => x.CategoryPageCacheLengthMinutes, storeScope);
            model.ProductDetailsPageCacheLengthMinutes_OverrideForStore = await settingService.SettingExistsAsync(settings, x => x.ProductDetailsPageCacheLengthMinutes, storeScope);
            
            model.Enabled_OverrideForStore = await settingService.SettingExistsAsync(settings, x => x.Enabled, storeScope);
            model.PageModifyingCustomerRoleIds_OverrideForStore = await settingService.SettingExistsAsync(settings, x => x.PageModifyingCustomerRoleIds, storeScope);
        }

        return View("~/Plugins/Apt.Misc.Booster/Views/Configure.cshtml", model);
    }

    [HttpPost("admin/plugin/booster/configure")]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Configure(ConfigurationModel model)
    {
        if (!ModelState.IsValid)
            return await Configure();

        //load settings for a chosen store scope
        var storeScope = await storeContext.GetActiveStoreScopeConfigurationAsync();
        var settings = await settingService.LoadSettingAsync<BoosterSettings>(storeScope);

        //save settings
        settings.ManufacturerPageCacheLengthMinutes = model.ManufacturerPageCacheLengthMinutes;
        settings.CategoryPageCacheLengthMinutes = model.CategoryPageCacheLengthMinutes;
        settings.ProductDetailsPageCacheLengthMinutes = model.ProductDetailsPageCacheLengthMinutes;
        
        settings.Enabled = model.Enabled;
        settings.PageModifyingCustomerRoleIds = model.PageModifyingCustomerRoleIds?.ToList() ?? new List<int>();
        

        /* We do not clear cache after each setting update.
         * This behavior can increase performance because cached settings will not be cleared
         * and loaded from database after each update */
        await settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.ManufacturerPageCacheLengthMinutes, model.ManufacturerPageCacheLengthMinutes_OverrideForStore, storeScope, false);
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
