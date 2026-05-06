using Apt.Nop.Plugin.Misc.TopicsPlus.Factories;
using Apt.Nop.Plugin.Misc.TopicsPlus.Models;
using Microsoft.AspNetCore.Mvc;
using Nop.Services.Security;
using Nop.Web.Areas.Admin.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Apt.Nop.Plugin.Misc.TopicsPlus.Controllers.Admin;

public class TopicRevisionController : BaseAdminController
{
    private readonly IPermissionService _permissionService;
    private readonly ITopicRevisionModelFactory _topicRevisionModelFactory;

    public TopicRevisionController(IPermissionService permissionService,
        ITopicRevisionModelFactory topicRevisionModelFactory)
    {
        _permissionService = permissionService;
        _topicRevisionModelFactory = topicRevisionModelFactory;
    }

    [HttpPost]
    [CheckPermission(StandardPermission.ContentManagement.TOPICS_CREATE_EDIT_DELETE)]
    public async Task<IActionResult> List(TopicRevisionSearchModel searchModel)
    {
        var model = await _topicRevisionModelFactory.PrepareTopicRevisionListModelAsync(searchModel);

        return Json(model);
    }


    [HttpPost]
    [CheckPermission(StandardPermission.ContentManagement.TOPICS_CREATE_EDIT_DELETE)]
    public async Task<IActionResult> Revert(int topicRevisionId)
    {
        if (!await _permissionService.AuthorizeAsync(StandardPermission.ContentManagement.TOPICS_CREATE_EDIT_DELETE))
            return await AccessDeniedJsonAsync();

   

        return Json(new{success = true});
    }

}