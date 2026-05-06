using Apt.Nop.Plugin.Misc.TopicsPlus.Models;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Apt.Nop.Plugin.Misc.TopicsPlus.Controllers.Admin;

[AuthorizeAdmin]
[Area(AreaNames.ADMIN)]
[AutoValidateAntiforgeryToken]
public class TopicsPlusConfigurationController : BasePluginController
{
    private readonly ILocalizationService _localizationService;
    private readonly INotificationService _notificationService;
    private readonly IPermissionService _permissionService;
    private readonly ISettingService _settingService;
    private readonly IStoreContext _storeContext;

    public TopicsPlusConfigurationController(
        ILocalizationService localizationService,
        INotificationService notificationService,
        IPermissionService permissionService,
        ISettingService settingService,
        IStoreContext storeContext)
    {
        _localizationService = localizationService;
        _notificationService = notificationService;
        _permissionService = permissionService;
        _settingService = settingService;
        _storeContext = storeContext;
    }

    [HttpGet("admin/apt/topics-plus/configure")]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]      
    public async Task<IActionResult> Configure()
    {
        var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
        var settings = await _settingService.LoadSettingAsync<TopicsPlusSettings>(storeScope);

        var model = new TopicsPlusConfigurationModel
        {
            ActiveStoreScopeConfiguration = storeScope,
            RevisionRetentionDays = settings.RevisionRetentionDays
        };

        if (storeScope > 0)
        {
            model.RevisionRetentionDays_OverrideForStore =
                await _settingService.SettingExistsAsync(settings, x => x.RevisionRetentionDays, storeScope);
        }

        return View($"{TopicsPlusConstants.PathToPlugin}/Views/Admin/Configure.cshtml", model);
    }

    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    [HttpPost("admin/apt/topics-plus/configure")]
    public async Task<IActionResult> Configure(TopicsPlusConfigurationModel model)
    {

        var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
        var settings = await _settingService.LoadSettingAsync<TopicsPlusSettings>(storeScope);

        settings.RevisionRetentionDays = model.RevisionRetentionDays;

        await _settingService.SaveSettingOverridablePerStoreAsync(
            settings,
            x => x.RevisionRetentionDays,
            model.RevisionRetentionDays_OverrideForStore,
            storeScope,
            false);

        await _settingService.ClearCacheAsync();

        _notificationService.SuccessNotification(
            await _localizationService.GetResourceAsync("Admin.Plugins.Saved"));

        return await Configure();
    }
}