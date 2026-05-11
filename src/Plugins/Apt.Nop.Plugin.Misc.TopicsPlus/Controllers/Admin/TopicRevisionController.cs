using Apt.Nop.Plugin.Misc.TopicsPlus.Factories;
using Apt.Nop.Plugin.Misc.TopicsPlus.Models;
using Apt.Nop.Plugin.Misc.TopicsPlus.Services;
using DocumentFormat.OpenXml.Office2010.Drawing;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Services.Messages;
using Nop.Services.Security;
using Nop.Services.Topics;
using Nop.Web.Areas.Admin.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Apt.Nop.Plugin.Misc.TopicsPlus.Controllers.Admin;

public class TopicRevisionController : BaseAdminController
{
    private readonly IPermissionService _permissionService;
    private readonly ITopicRevisionModelFactory _topicRevisionModelFactory;
    private readonly ITopicRevisionService _topicRevisionService;
    private readonly ICustomTopicService _topicService;
    private readonly IWorkContext _workContext;
    private readonly INotificationService _notificationService;

    public TopicRevisionController(IPermissionService permissionService,
        ITopicRevisionModelFactory topicRevisionModelFactory, ICustomTopicService topicService, ITopicRevisionService topicRevisionService, IWorkContext workContext, INotificationService notificationService)
    {
        _permissionService = permissionService;
        _topicRevisionModelFactory = topicRevisionModelFactory;
        _topicService = topicService;
        _topicRevisionService = topicRevisionService;
        _workContext = workContext;
        _notificationService = notificationService;
    }

    [HttpPost("admin/apt/topics-plus/revision-list")]
    [CheckPermission(StandardPermission.ContentManagement.TOPICS_CREATE_EDIT_DELETE)]
    public async Task<IActionResult> List(TopicRevisionSearchModel searchModel)
    {
        var model = await _topicRevisionModelFactory.PrepareTopicRevisionListModelAsync(searchModel);

        return Json(model);
    }


    [HttpPost("admin/apt/topics-plus/revert/{topicId}/{topicRevisionId}")]
    [CheckPermission(StandardPermission.ContentManagement.TOPICS_CREATE_EDIT_DELETE)]
    public async Task<IActionResult> Revert(int topicRevisionId, int topicId)
    {
        if (!await _permissionService.AuthorizeAsync(StandardPermission.ContentManagement.TOPICS_CREATE_EDIT_DELETE))
            return await AccessDeniedJsonAsync();

        var customer = await _workContext.GetCurrentCustomerAsync();

        var topic = await _topicService.GetTopicByIdAsync(topicId);
        var topicRevision = await _topicRevisionService.GetTopicRevisionByIdAsync(topicRevisionId);

        topic.Body = topicRevision.Body;
        topic.Title = topicRevision.Title;

        await _topicService.UpdateTopicAsync(topic, false);
        await _topicRevisionService.CreateRevisionAsync(topic, customer.Id, topicRevisionId);

        _notificationService.SuccessNotification($"Topic has been reverted to version {topicRevision.Version}");

        return Json(new{success = true});
    }



}