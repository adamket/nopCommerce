using Apt.Nop.Plugin.Misc.TopicsPlus.Models;
using Apt.Nop.Plugin.Misc.TopicsPlus.Services;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Security;
using Nop.Services.Topics;
using Nop.Web.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Apt.Nop.Plugin.Misc.TopicsPlus.Controllers;

[AutoValidateAntiforgeryToken]
public class TopicsPlusController : BasePublicController
{
    #region Fields

    protected readonly ILanguageService _languageService;
    protected readonly ILocalizationService _localizationService;
    protected readonly INotificationService _notificationService;
    protected readonly IPermissionService _permissionService;
    protected readonly ISettingService _settingService;
    protected readonly IStoreContext _storeContext;
    protected readonly ITopicService _topicService;
    protected readonly ITopicRevisionService _topicRevisionService;


    #endregion

    #region Ctor

    public TopicsPlusController(ILanguageService languageService,
        ILocalizationService localizationService,
        INotificationService notificationService,
        IPermissionService permissionService,
        ISettingService settingService,
        IStoreContext storeContext, ITopicService topicService, ITopicRevisionService topicRevisionService)
    {
        _languageService = languageService;
        _localizationService = localizationService;
        _notificationService = notificationService;
        _permissionService = permissionService;
        _settingService = settingService;
        _storeContext = storeContext;
        _topicService = topicService;
        _topicRevisionService = topicRevisionService;
    }

    #endregion

    #region Methods

    [HttpPost("apt/topics-plus/update-topic")]
    [CheckPermission(StandardPermission.ContentManagement.TOPICS_CREATE_EDIT_DELETE)]
    public async Task<IActionResult> UpdateTopic(int topicId, string content, string title)
    {
        var topic = await _topicService.GetTopicByIdAsync(topicId);
        if (topic == null)
        {
            return Json(new
            {
                message = "Not found.",
            });
        }

        //var storeId = await _storeContext.GetCurrentStoreIdAsync();
        topic.Body = content;
        topic.Title = title;
        await _topicService.UpdateTopicAsync(topic);

        return Json(new
        {
            success = true
        });
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


    #endregion
}