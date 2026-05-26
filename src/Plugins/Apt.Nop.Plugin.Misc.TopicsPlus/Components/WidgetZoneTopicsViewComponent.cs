using Apt.Nop.Plugin.Misc.TopicsPlus.Models;
using Apt.Nop.Plugin.Misc.TopicsPlus.Services;
using LinqToDB;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Services.Localization;
using Nop.Web.Factories;
using Nop.Web.Framework.Components;
using Nop.Web.Models.Topics;

namespace Apt.Nop.Plugin.Misc.TopicsPlus.Components;

public class WidgetZoneTopicsViewComponent(
    TopicsPlusSettings topicsPlusSettings,
    ILocalizationService localizationService,
    IStoreContext storeContext,
    IWorkContext workContext,
    ICustomTopicService topicService,
    ITopicModelFactory topicModelFactory)
    : NopViewComponent
{

    /// <returns>A task that represents the asynchronous operation</returns>
    public async Task<IViewComponentResult> InvokeAsync(string widgetZone)
    {
        var topicData = await topicService.GetAllTopicDataAsync(widgetZone);
        var topicIds = topicData.Select(q => q.TopicId);

        var topics = (await topicService.GetAllTopicsAsync((await storeContext.GetCurrentStoreAsync()).Id)).Where(q=>topicIds.Contains(q.Id));
        var topicModels = new List<TopicModel>();

        foreach (var topic in topics)
        {
            var topicModel = await topicModelFactory.PrepareTopicModelAsync(topic);
            topicModels.Add(topicModel);
        }

        var model = new WidgetZoneTopicModel
        {
            Topics = topicModels
        };

        return View($"{TopicsPlusConstants.PathToPlugin}/Views/Shared/Components/WidgetZone/Default.cshtml", model);
    }
}