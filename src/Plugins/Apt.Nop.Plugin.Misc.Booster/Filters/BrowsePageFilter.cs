using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Nop.Core;
using Nop.Core.Caching;
using Nop.Core.Domain.Customers;
using Nop.Services.Common;
using Nop.Services.Security;
using Nop.Web.Framework.UI;

namespace Apt.Nop.Plugin.Misc.Booster.Filters;
internal abstract class BrowsePageFilter<TModel>(
    BoosterSettings settings,
    IStaticCacheManager cacheManager,
    IStoreContext storeContext,
    IWorkContext workContext,
    IPermissionService permissionService,
    INopHtmlHelper nopHtmlHelper,
    IUrlHelperFactory urlHelperFactory,
    IWebHelper webHelper,
    IGenericAttributeService genericAttributeService
)
    : BasePageFilter<TModel>(settings, cacheManager, storeContext, workContext, permissionService, nopHtmlHelper, urlHelperFactory, webHelper)
where TModel : class
{
    protected override async Task OnCacheHitAsync(ActionExecutingContext ctx, int id)
    {
        var customer = await workContext.GetCurrentCustomerAsync();
        var store = await storeContext.GetCurrentStoreAsync();
        var thisUrl = webHelper.GetThisPageUrl(false);

        var lastUrl = await genericAttributeService.GetAttributeAsync<string>(
            customer, NopCustomerDefaults.LastContinueShoppingPageAttribute, store.Id);

        if (thisUrl != lastUrl)
            await genericAttributeService.SaveAttributeAsync(
                customer, NopCustomerDefaults.LastContinueShoppingPageAttribute,
                thisUrl, store.Id);
    }
}
