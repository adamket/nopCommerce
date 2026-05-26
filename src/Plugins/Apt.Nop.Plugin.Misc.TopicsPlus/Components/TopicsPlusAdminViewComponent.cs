using Apt.Nop.Plugin.Misc.TopicsPlus.Models;
using Apt.Nop.Plugin.Misc.TopicsPlus.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Core;
using Nop.Services.Localization;
using Nop.Web.Areas.Admin.Models.Topics;
using Nop.Web.Framework.Components;

namespace Apt.Nop.Plugin.Misc.TopicsPlus.Components;

public class TopicsPlusAdminViewComponent(
    ILocalizationService localizationService,
    IStoreContext storeContext,
    IWorkContext workContext,
    ICustomTopicService topicService)
    : NopViewComponent
{

    /// <returns>A task that represents the asynchronous operation</returns>
    public async Task<IViewComponentResult> InvokeAsync(TopicModel additionalData)
    {
        var widgetZones = WidgetZoneHelper.GetPublicWidgetZones();

        var topicData = await topicService.GetTopicDataByTopicIdAsync(additionalData.Id);
        var model = new TopicsPlusAdminModel
        {
            TopicRevisionSearchModel = new TopicRevisionSearchModel
            {
                SearchTopicId = additionalData.Id
            },
            TopicDataModel = new TopicDataModel
            {
                TopicId = additionalData.Id,
                AvailableWidgetZones = widgetZones.Select(q=> new SelectListItem(q, q)).ToList(),
                SelectedWidgetZones = topicData?.WidgetZones?.Split(",", StringSplitOptions.RemoveEmptyEntries).ToList() ?? []
            }
        };

        return View($"{TopicsPlusConstants.PathToPlugin}/Views/Admin/Shared/Components/TopicsPlus/Default.cshtml", model);
    }
}