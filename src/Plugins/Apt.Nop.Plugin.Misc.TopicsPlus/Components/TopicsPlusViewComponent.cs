using Apt.Nop.Plugin.Misc.TopicsPlus.Models;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Services.Localization;
using Nop.Web.Areas.Admin.Models.Topics;
using Nop.Web.Framework.Components;

namespace Apt.Nop.Plugin.Misc.TopicsPlus.Components;

public class TopicsPlusViewComponent : NopViewComponent
{
    protected readonly TopicsPlusSettings _checkMoneyOrderPaymentSettings;
    protected readonly ILocalizationService _localizationService;
    protected readonly IStoreContext _storeContext;
    protected readonly IWorkContext _workContext;

    public TopicsPlusViewComponent(TopicsPlusSettings checkMoneyOrderPaymentSettings,
        ILocalizationService localizationService,
        IStoreContext storeContext,
        IWorkContext workContext)
    {
        _checkMoneyOrderPaymentSettings = checkMoneyOrderPaymentSettings;
        _localizationService = localizationService;
        _storeContext = storeContext;
        _workContext = workContext;
    }

    /// <returns>A task that represents the asynchronous operation</returns>
    public async Task<IViewComponentResult> InvokeAsync(TopicModel additionalData)
    {
        var searchModel = new TopicRevisionSearchModel
        {
            SearchTopicId = additionalData.Id
        };
        return View($"{TopicsPlusConstants.PathToPlugin}/Views/Admin/Shared/Components/TopicsPlus/Default.cshtml", searchModel);
    }
}