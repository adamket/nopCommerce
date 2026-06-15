using Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Microsoft.AspNetCore.Mvc;
using Nop.Services.Messages;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Controllers;

[AuthorizeAdmin]
[Area(AreaNames.ADMIN)]
[AutoValidateAntiforgeryToken]
public class AkeneoMappingController : BasePluginController
{

    private readonly IAkeneoMappingModelFactory _mappingModelFactory;
    private readonly IAkeneoAttributeMappingService _akeneoAttributeMappingService;
    private readonly INotificationService _notificationService;

    public AkeneoMappingController(
        IAkeneoMappingModelFactory mappingModelFactory,
        IAkeneoAttributeMappingService akeneoAttributeMappingService,
        INotificationService notificationService)
    {
        _mappingModelFactory = mappingModelFactory;
        _akeneoAttributeMappingService = akeneoAttributeMappingService;
        _notificationService = notificationService;
    }

    public async Task<IActionResult> AttributeMappings()
    {
        var model = await _mappingModelFactory.PrepareAttributeMappingListModelAsync();

        return View($"{AkeneoConstants.PathToPlugin}/Views/AttributeMappings.cshtml", model);
    }

    [HttpPost]
    public async Task<IActionResult> AttributeMappings(AkeneoAttributeMappingListModel model)
    {
        if (!ModelState.IsValid)
            return await PrepareAttributeMappingsViewAsync(model);

        var validationResult = await _akeneoAttributeMappingService.ValidateAttributeMappingsAsync(model);

        if (!validationResult.Success)
        {
            foreach (var error in validationResult.Errors)
                ModelState.AddModelError(string.Empty, error);

            return await PrepareAttributeMappingsViewAsync(model);
        }

        await _akeneoAttributeMappingService.SaveAttributeMappingsAsync(model);

        _notificationService.SuccessNotification("Akeneo attribute mappings saved successfully.");

        return RedirectToAction(nameof(AttributeMappings));
    }

    private async Task<IActionResult> PrepareAttributeMappingsViewAsync(
        AkeneoAttributeMappingListModel model)
    {
        model = await _mappingModelFactory.PrepareAttributeMappingListModelAsync(model);

        return null; // View(AttributeMappingsViewPath, model);
    }
}