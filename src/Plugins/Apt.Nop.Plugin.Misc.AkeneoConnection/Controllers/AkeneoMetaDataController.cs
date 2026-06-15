//using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
//using Microsoft.AspNetCore.Mvc;
//using Nop.Services.Messages;
//using Nop.Web.Framework;
//using Nop.Web.Framework.Controllers;
//using Nop.Web.Framework.Mvc.Filters;

//[AuthorizeAdmin]
//[Area(AreaNames.ADMIN)]
//[AutoValidateAntiforgeryToken]
//public class AkeneoMetadataController : BasePluginController
//{
//    private readonly IAkeneoMetadataSyncService _metadataSyncService;
//    private readonly INotificationService _notificationService;

//    public AkeneoMetadataController(
//        IAkeneoMetadataSyncService metadataSyncService,
//        INotificationService notificationService)
//    {
//        _metadataSyncService = metadataSyncService;
//        _notificationService = notificationService;
//    }

//    public async Task<IActionResult> Index()
//    {
//        var model = await _metadataSyncService.PrepareMetadataStatusModelAsync();

//        return View("~/Plugins/Misc.Akeneo/Views/Admin/Metadata/Index.cshtml", model);
//    }

//    [HttpPost]
//    public async Task<IActionResult> RefreshAll()
//    {
//        await _metadataSyncService.RefreshAllAsync();

//        _notificationService.SuccessNotification("Akeneo metadata refreshed successfully.");

//        return RedirectToAction(nameof(Index));
//    }
//}