using Apt.Nop.Plugin.Misc.PageCache;
using Apt.Nop.Plugin.Misc.PageCache.Extensions;
using Apt.Nop.Plugin.Misc.PageCache.Types;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Primitives;
using Nop.Core;
using Nop.Core.Caching;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Services.Discounts;
using Nop.Services.Logging;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.UI;
using Nop.Web.Models.Catalog;

namespace Apt.Nop.Plugin.Misc.PageCache.Filters
{
    /// <summary>
    /// Represents a filter attribute that confirms access to a closed store
    /// </summary>
    public sealed class ProductDetailsActionAttribute : TypeFilterAttribute
    {
        #region Ctor

        /// <summary>
        /// Create instance of the filter attribute
        /// </summary>
        /// <param name="ignore">Whether to ignore the execution of filter actions</param>
        public ProductDetailsActionAttribute() : base(typeof(ProductDetailsFilter))
        {

        }

        #endregion

        /// <summary>
        /// Represents a filter that confirms access to closed store
        /// </summary>
        private class ProductDetailsFilter(
            PageCacheSettings pageCacheSettings,
            IStaticCacheManager staticCacheManager,
            IRecentlyViewedProductsService recentlyViewedProductsService,
            IStoreContext storeContext,
            IWorkContext workContext,
            IPermissionService permissionService,
            INopHtmlHelper nopHtmlHelper,
            IUrlHelperFactory urlHelperFactory,
            IWebHelper webHelper,
            ICustomerService customerService,
            IProductService productService,
            ILogger logger,
            IDiscountService discountService)
            : IAsyncActionFilter
        {
            #region Fields

            private readonly PageCacheSettings _pageCacheSettings = pageCacheSettings;
            private readonly ICustomerService _customerService = customerService;
            private readonly IProductService _productService = productService;
            private readonly ILogger _logger = logger;

            #endregion

            #region Ctor

            #endregion

            #region Utilities

            private async Task CustomOnActionExecuting(ActionExecutingContext filterContext)
            {
                if (!pageCacheSettings.Enabled)
                    return;

                var isPdpAction = filterContext.IsAction("Product", "ProductDetails");
                if (!isPdpAction)
                    return;

                var urlHelper = urlHelperFactory.GetUrlHelper(filterContext);

                var containsUpdateCartItemId = filterContext.ActionArguments.ContainsKey("updatecartitemid")
                                               && Convert.ToInt32(
                                                   filterContext.ActionArguments["updatecartitemid"] ?? "0") > 0;

                //var containsCouponCodes =
                //    filterContext.HttpContext.Request.Query.TryGetValue(NopDiscountDefaults.DiscountCouponQueryParameter,
                //        out var couponCodes) && !StringValues.IsNullOrEmpty(couponCodes);

                var containsProductId = filterContext.ActionArguments.ContainsKey("productId") &&
                                        filterContext.ActionArguments["productId"] != null;

                if (!containsProductId
                    || containsUpdateCartItemId
                   /* || containsCouponCodes*/)
                { return; }

                var prodId = Convert.ToInt32(filterContext.ActionArguments["productId"]);

                await recentlyViewedProductsService.AddProductToRecentlyViewedListAsync(prodId);

                var rolesStr = await (await workContext.GetCurrentCustomerAsync()).GetCustomerRoleIdsStrDescAsync(_customerService, _pageCacheSettings);
                var cacheKey = new CacheKey(string.Format(PageCacheConstants.PDP_CACHE_KEY_FORMAT, prodId, (await storeContext.GetCurrentStoreAsync()).Id, rolesStr));
                var cachedModel = await staticCacheManager.GetAsync<ActionResultCacheItem<ProductDetailsModel>>(cacheKey, async () => null);
                if (cachedModel != null)
                {
                    var result = cachedModel.GetResult<ViewResult>();

                    //display "edit" (manage) link
                    if (await permissionService.AuthorizeAsync(StandardPermission.Security.ACCESS_ADMIN_PANEL) &&
                        await permissionService.AuthorizeAsync(StandardPermission.Catalog.PRODUCTS_CREATE_EDIT_DELETE))
                    {
                        nopHtmlHelper.AddEditPageUrl(urlHelper.Action("Edit", "Product",
                            new { id = prodId, area = AreaNames.ADMIN }));
                    }
                    filterContext.Result = result;
                    return;
                }

                return;
            }


            private async Task CustomOnActionExecuted(ActionExecutedContext filterContext)
            {
                var store = await storeContext.GetCurrentStoreAsync();

                var model = filterContext.Result.GetModel<ProductDetailsModel>();
                if (model == null)  
return;                   
                var customer = await workContext.GetCurrentCustomerAsync();
                 
                var appliedDiscountCodes = await customerService.ParseAppliedDiscountCouponCodesAsync(customer);
                if (appliedDiscountCodes.Any())
                    return;

                if (webHelper.QueryString<string>("updatecartitemid") != null)
                    return;


                //cache  result
                var pdpResult = new ActionResultCacheItem<ProductDetailsModel>(filterContext.Result);

                var rolesStr = await (await workContext.GetCurrentCustomerAsync()).GetCustomerRoleIdsStrDescAsync();


                var cacheKey = new CacheKey(string.Format(PageCacheConstants.PDP_CACHE_KEY_FORMAT,
                    pdpResult.Model.Id, store.Id, rolesStr))
                {
                    CacheTime = pageCacheSettings.ProductDetailsPageCacheLengthMinutes,
                    // Prefixes = { AdfConstants.CacheKeys.ProductDetailsPrefix + pdpResult.Model.Id }
                };

                await staticCacheManager.SetAsync(cacheKey, pdpResult);

            }


            public async Task OnActionExecutionAsync(
                ActionExecutingContext filterContext,
                ActionExecutionDelegate next)
            {
                try
                {
                    await CustomOnActionExecuting(filterContext);
                    if (filterContext.Result == null)
                    {
                        var resultContext = await next();
                        await CustomOnActionExecuted(resultContext);

                    }

                }
                finally
                {
                }
            }
        }
        #endregion
    }
}