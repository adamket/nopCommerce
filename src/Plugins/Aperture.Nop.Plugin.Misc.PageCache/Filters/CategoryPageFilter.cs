using Aperture.Nop.Plugin.Misc.PageCache.Extensions;
using Aperture.Nop.Plugin.Misc.PageCache.Types;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Primitives;
using Nop.Core;
using Nop.Core.Caching;
using Nop.Core.Domain.Customers;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Discounts;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.UI;
using Nop.Web.Models.Catalog;

namespace Aperture.Nop.Plugin.Misc.PageCache.Filters
{
    /// <summary>
    /// Represents a filter attribute that confirms access to a closed store
    /// </summary>
    public sealed class CategoryActionAttribute : TypeFilterAttribute
    {
        #region Ctor

        /// <summary>
        /// Create instance of the filter attribute
        /// </summary>
        /// <param name="ignore">Whether to ignore the execution of filter actions</param>
        public CategoryActionAttribute() : base(typeof(CategoryFilter))
        {

        }

        #endregion

        #region Properties


        #endregion


        /// <summary>
        /// Represents a filter that confirms access to closed store
        /// </summary>
        private class CategoryFilter(
            PageCacheSettings pageCacheSettings,
            IStaticCacheManager staticCacheManager,
            IRecentlyViewedProductsService recentlyViewedProductsService,
            IStoreContext storeContext,
            IWorkContext workContext,
            IPermissionService permissionService,
            INopHtmlHelper nopHtmlHelper,
            IUrlHelperFactory urlHelperFactory,
            IWebHelper webHelper,
            IGenericAttributeService genericAttributeService)
            : IAsyncActionFilter
        {
     
            #region Utilities

            private async Task CustomOnActionExecuting(ActionExecutingContext filterContext)
            {
                if (!pageCacheSettings.Enabled)
                {
                    return;
                }

                var isCategoryPageAction = filterContext.IsAction("Catalog", "Category");
                if (!isCategoryPageAction)
                {
                    return;
                }

                var urlHelper = urlHelperFactory.GetUrlHelper(filterContext);

                var currentCustomer = await workContext.GetCurrentCustomerAsync();
                var currentStore = await storeContext.GetCurrentStoreAsync();

                var containsCategoryId = filterContext.ActionArguments.ContainsKey("categoryId") &&
                                         filterContext.ActionArguments["categoryId"] != null;

                var containsCouponCodes =
                    filterContext.HttpContext.Request.Query.TryGetValue(
                        NopDiscountDefaults.DiscountCouponQueryParameter,
                        out var couponCodes) && !StringValues.IsNullOrEmpty(couponCodes);

                var containsQueryParameter =
                    filterContext.HttpContext.Request.Query.Any(); //TODO why did I do this again?

                if (!containsCategoryId
                    || containsCouponCodes
                    || containsQueryParameter) { return; }

                var catId = Convert.ToInt32(filterContext.ActionArguments["categoryId"]);
                var rolesStr = await currentCustomer.GetCustomerRoleIdsStrDescAsync();
                var cacheKey =
                    new CacheKey(string.Format(PageCacheConstants.CATEGORY_PAGE_CACHE_KEY_FORMAT, catId, currentStore.Id,
                            rolesStr))
                        { CacheTime = int.MaxValue };
                var cachedModel = await
                    staticCacheManager.GetAsync<ActionResultCacheItem<CategoryModel>>(cacheKey, async () => null);
                if (cachedModel != null)
                {
                    var lastShoppingUrl = await genericAttributeService.GetAttributeAsync<string>(currentCustomer,
                        NopCustomerDefaults.LastContinueShoppingPageAttribute, currentStore.Id);

                    var thisUrl = webHelper.GetThisPageUrl(false);
                    if (thisUrl != lastShoppingUrl)
                    {
                        await genericAttributeService.SaveAttributeAsync(currentCustomer,
                            NopCustomerDefaults.LastContinueShoppingPageAttribute,
                            webHelper.GetThisPageUrl(false),
                            currentStore.Id);
                    }

                    var result = cachedModel.GetResult<ViewResult>();

                    if (await permissionService.AuthorizeAsync(StandardPermission.Security.ACCESS_ADMIN_PANEL) &&
                        await permissionService.AuthorizeAsync(StandardPermission.Catalog.CATEGORIES_CREATE_EDIT_DELETE))
                    {
                        //display "edit" (manage) link
                        nopHtmlHelper.AddEditPageUrl(urlHelper.Action("Edit", "Category",
                            new { id = catId, area = AreaNames.ADMIN }));
                    }

                    filterContext.Result = result;
                    return;
                }

                return;
            }

            private async Task CustomOnActionExecuted(ActionExecutedContext filterContext)
            {
                var currentCustomer = await workContext.GetCurrentCustomerAsync();
                var currentStore = await storeContext.GetCurrentStoreAsync();

                var containsQueryParameter =
                    filterContext.HttpContext.Request.Query.Any();

                if (containsQueryParameter)
                {
                    return;
                }

                var model = filterContext.Result.GetModel<CategoryModel>();
                if (model == null)
                { return; }

                //cache  result
                var categoryPageResult = new ActionResultCacheItem<CategoryModel>(filterContext.Result);

                var rolesStr = await currentCustomer.GetCustomerRoleIdsStrDescAsync();
                var cacheKey = new CacheKey(string.Format(PageCacheConstants.CATEGORY_PAGE_CACHE_KEY_FORMAT, categoryPageResult.Model.Id, currentStore.Id, rolesStr))
                {
                    CacheTime = pageCacheSettings.CategoryPageCacheLengthMinutes,
                };

                await staticCacheManager.SetAsync(cacheKey, categoryPageResult);
            }


            public async Task OnActionExecutionAsync(
                ActionExecutingContext filterContext,
                ActionExecutionDelegate next)
            {
                try
                {
                    await this.CustomOnActionExecuting(filterContext);
                    if (filterContext.Result == null)
                    {
                        var resultContext = await next();
                        await this.CustomOnActionExecuted(resultContext);

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