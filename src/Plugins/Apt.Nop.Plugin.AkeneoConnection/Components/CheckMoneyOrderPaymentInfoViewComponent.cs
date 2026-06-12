using Apt.Nop.Plugin.AkeneoConnection;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Payments.CheckMoneyOrder.Models;
using Nop.Services.Localization;
using Nop.Web.Framework.Components;

namespace Apt.Nop.Plugin.AkeneoConnection.Components;

public class CheckMoneyOrderViewComponent : NopViewComponent
{
    protected readonly AkeneoConnectionSettings _checkMoneyOrderPaymentSettings;
    protected readonly ILocalizationService _localizationService;
    protected readonly IStoreContext _storeContext;
    protected readonly IWorkContext _workContext;

    public CheckMoneyOrderViewComponent(AkeneoConnectionSettings checkMoneyOrderPaymentSettings,
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
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var store = await _storeContext.GetCurrentStoreAsync();

        var model = new PaymentInfoModel
        {
            DescriptionText = await _localizationService.GetLocalizedSettingAsync(_checkMoneyOrderPaymentSettings,
                x => x.DescriptionText, (await _workContext.GetWorkingLanguageAsync()).Id, store.Id)
        };

        return View("~/Plugins/Payments.CheckMoneyOrder/Views/PaymentInfo.cshtml", model);
    }
}