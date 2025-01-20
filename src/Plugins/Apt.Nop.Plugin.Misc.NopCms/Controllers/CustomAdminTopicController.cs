using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Apt.Nop.Plugin.Misc.NopCms.Services;
using Microsoft.AspNetCore.Mvc;
using Nop.Core.Domain.Customers;
using Nop.Core;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Messages;
using Nop.Services.Security;
using Nop.Services.Seo;
using Nop.Services.Stores;
using Nop.Services.Topics;
using Nop.Web.Areas.Admin.Controllers;
using Nop.Web.Areas.Admin.Factories;
using Nop.Web.Areas.Admin.Infrastructure.Mapper.Extensions;
using Nop.Web.Areas.Admin.Models.Topics;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Apt.Nop.Plugin.Misc.NopCms.Controllers;
public class CustomAdminTopicController : TopicController
{

    private new readonly IAclService _aclService;
    private new readonly ICustomerActivityService _customerActivityService;
    private new readonly ICustomerService _customerService;
    private new readonly ILocalizationService _localizationService;
    private new readonly ILocalizedEntityService _localizedEntityService;
    private new readonly INotificationService _notificationService;
    private new readonly IPermissionService _permissionService;
    private new readonly IStoreMappingService _storeMappingService;
    private new readonly IStoreService _storeService;
    private new readonly ITopicModelFactory _topicModelFactory;
    private new readonly ITopicService _topicService;
    private new readonly IUrlRecordService _urlRecordService;
    private new readonly IGenericAttributeService _genericAttributeService;
    private new readonly IWorkContext _workContext;


    public CustomAdminTopicController(TopicEntryService topicEntryService, IAclService aclService, ICustomerActivityService customerActivityService, ICustomerService customerService, ILocalizationService localizationService, ILocalizedEntityService localizedEntityService, INotificationService notificationService, IPermissionService permissionService, IStoreMappingService storeMappingService, IStoreService storeService, ITopicModelFactory topicModelFactory, ITopicService topicService, IUrlRecordService urlRecordService, IGenericAttributeService genericAttributeService, IWorkContext workContext) : base(aclService, customerActivityService, customerService, localizationService, localizedEntityService, notificationService, permissionService, storeMappingService, storeService, topicModelFactory, topicService, urlRecordService, genericAttributeService, workContext)
    {
        _aclService = aclService;
        _customerActivityService = customerActivityService;
        _customerService = customerService;
        _localizationService = localizationService;
        _localizedEntityService = localizedEntityService;
        _notificationService = notificationService;
        _permissionService = permissionService;
        _storeMappingService = storeMappingService;
        _storeService = storeService;
        _topicModelFactory = topicModelFactory;
        _topicService = topicService;
        _urlRecordService = urlRecordService;
        _genericAttributeService = genericAttributeService;
        _workContext = workContext;
    }

    [HttpGet("admin/topic/edit/{id}")]
    [CheckPermission(StandardPermission.ContentManagement.TOPICS_VIEW)]
    public async Task<IActionResult> Edit(int id, bool showtour = false, int? v = null)
    {
        //try to get a topic with the specified id
        var topic = await _topicService.GetTopicByIdAsync(id);
        if (topic == null)
            return RedirectToAction("List");

        //prepare model
        var model = await _topicModelFactory.PrepareTopicModelAsync(null, topic);

        //show configuration tour
        if (showtour)
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            var hideCard = await _genericAttributeService.GetAttributeAsync<bool>(customer, NopCustomerDefaults.HideConfigurationStepsAttribute);
            var closeCard = await _genericAttributeService.GetAttributeAsync<bool>(customer, NopCustomerDefaults.CloseConfigurationStepsAttribute);

            if (!hideCard && !closeCard)
                ViewBag.ShowTour = true;
        }

        return View("~/areas/admin/views/topic/edit.cshtml", model);
    }

    [HttpPost("admin/topic/custom-edit")]
    [ParameterBasedOnFormName("save-continue", "continueEditing")]
    [CheckPermission(StandardPermission.ContentManagement.TOPICS_CREATE_EDIT_DELETE)]
    public  async Task<IActionResult> CustomEdit(TopicModel model, bool continueEditing)
    {
        //try to get a topic with the specified id
        var topic = await _topicService.GetTopicByIdAsync(model.Id);
        if (topic == null)
            return RedirectToAction("List");

        if (!model.IsPasswordProtected)
            model.Password = null;

        if (ModelState.IsValid)
        {
            topic = model.ToEntity(topic);
            await _topicService.UpdateTopicAsync(topic);

            //search engine name
            model.SeName = await _urlRecordService.ValidateSeNameAsync(topic, model.SeName, topic.Title ?? topic.SystemName, true);
            await _urlRecordService.SaveSlugAsync(topic, model.SeName, 0);

            //stores
            await SaveStoreMappingsAsync(topic, model);

            //locales
            await UpdateLocalesAsync(topic, model);

            _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Admin.ContentManagement.Topics.Updated"));

            //activity log
            await _customerActivityService.InsertActivityAsync("EditTopic",
                string.Format(await _localizationService.GetResourceAsync("ActivityLog.EditTopic"), topic.Title ?? topic.SystemName), topic);

            if (!continueEditing)
                return RedirectToAction("List");

            return RedirectToAction("Edit", new { id = topic.Id });
        }

        //prepare model
        model = await _topicModelFactory.PrepareTopicModelAsync(model, topic, true);

        //if we got this far, something failed, redisplay form
        return View(model);
    }


}
