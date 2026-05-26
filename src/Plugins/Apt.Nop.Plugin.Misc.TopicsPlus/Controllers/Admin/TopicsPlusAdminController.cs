using Apt.Nop.Plugin.Misc.TopicsPlus.Factories;
using Apt.Nop.Plugin.Misc.TopicsPlus.Models;
using Apt.Nop.Plugin.Misc.TopicsPlus.Services;
using DocumentFormat.OpenXml.Office2010.Drawing;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Caching;
using Nop.Core.Events;
using Nop.Services.Messages;
using Nop.Services.Security;
using Nop.Services.Topics;
using Nop.Web.Areas.Admin.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Apt.Nop.Plugin.Misc.TopicsPlus.Controllers.Admin;

public class TopicsPlusAdminController : BaseAdminController
{
    private readonly IPermissionService _permissionService;
    private readonly ITopicRevisionModelFactory _topicRevisionModelFactory;
    private readonly ITopicRevisionService _topicRevisionService;
    private readonly ICustomTopicService _topicService;
    private readonly IWorkContext _workContext;
    private readonly INotificationService _notificationService;
    private readonly IEventPublisher _eventPublisher;

    public TopicsPlusAdminController(IPermissionService permissionService,
        ITopicRevisionModelFactory topicRevisionModelFactory, ICustomTopicService topicService, ITopicRevisionService topicRevisionService, IWorkContext workContext, INotificationService notificationService, IEventPublisher eventPublisher)
    {
        _permissionService = permissionService;
        _topicRevisionModelFactory = topicRevisionModelFactory;
        _topicService = topicService;
        _topicRevisionService = topicRevisionService;
        _workContext = workContext;
        _notificationService = notificationService;
        _eventPublisher = eventPublisher;
    }

    [HttpPost("admin/apt/topics-plus/revision-list")]
    [CheckPermission(StandardPermission.ContentManagement.TOPICS_CREATE_EDIT_DELETE)]
    public async Task<IActionResult> List(TopicRevisionSearchModel searchModel)
    {
        var model = await _topicRevisionModelFactory.PrepareTopicRevisionListModelAsync(searchModel);

        return Json(model);
    }


    [HttpPost("admin/apt/topics-plus/revert")]
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

        await _topicRevisionService.CreateRevisionAsync(topic, customer.Id, topicRevisionId);
        await _topicService.UpdateTopicAsync(topic);
        

        _notificationService.SuccessNotification($"Topic has been reverted to version {topicRevision.Version}");

        return Json(new{success = true});
    }

    [CheckPermission(StandardPermission.ContentManagement.TOPICS_CREATE_EDIT_DELETE)]
    [HttpGet("apt/topics-plus/preview/{id}")]
    public async Task<IActionResult> Preview(int id)
    {
        var revision = await _topicRevisionService.GetTopicRevisionByIdAsync(id);
        if (revision == null)
            return NotFound();

        var model = new TopicRevisionModel
        {
            Id = revision.Id,
            TopicId = revision.TopicId,
            Title = revision.Title,
            Body = revision.Body
        };

        return View($"{TopicsPlusConstants.PathToPlugin}/Views/Admin/Shared/Components/TopicsPlus/_TopicRevisionPreview.cshtml", model);
    }



    [CheckPermission(StandardPermission.ContentManagement.TOPICS_CREATE_EDIT_DELETE)]
    [HttpPost("admin/apt/topics-plus/update-topic-data/{topicId}")]
    public async Task<IActionResult> UpdateTopicData(int topicId, IList<string> widgetZones)
    {
        var topicData = await _topicService.GetTopicDataByTopicIdAsync(topicId);

        var widgetZonesStr = string.Join(",", widgetZones);
        if (topicData == null)
        {
            topicData = new Domain.TopicData
            {
                TopicId = topicId,
                WidgetZones = widgetZonesStr
            };
            await _topicService.InsertTopicDataAsync(topicData);
        }
        else
        {
            topicData.WidgetZones = widgetZonesStr;
            await _topicService.UpdateTopicDataAsync(topicData);
        }
        return Json(new { success = true });
    }
}