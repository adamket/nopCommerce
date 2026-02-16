using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Services.Configuration;
using Nop.Services.Messages;
using Nop.Web.Areas.Admin.Controllers;
using Nop.Web.Framework.Controllers;

namespace Aperture.Nop.Plugin.Misc.PageCache.Controllers.Admin;

/// <summary>
/// Admin controller for plugin configuration
/// </summary>
public class PageCacheController : BaseAdminController
{
    #region Fields

    private readonly ISettingService _settingService;
    private readonly INotificationService _notificationService;

    #endregion

    #region Ctor

    public PageCacheController(ISettingService settingService, INotificationService notificationService)
    {
        _settingService = settingService;
        _notificationService = notificationService;
    }

    #endregion

    #region Methods

    public async Task<IActionResult> Configure()
    {
        var settings = await _settingService.LoadSettingAsync<PageCacheSettings>();
        return View("~/Plugins/Aperture.Misc.PageCache/Views/Configure.cshtml", settings);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Configure(PageCacheSettings model)
    {
        if (!ModelState.IsValid)
            return await Configure();

        await _settingService.SaveSettingAsync(model);
        _notificationService.SuccessNotification("Settings saved.");
        return await Configure();
    }

    #endregion
}
