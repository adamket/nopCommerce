using Apt.Nop.Plugin.Misc.Booster.Extensions;
using Apt.Nop.Plugin.Misc.Booster.Types;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
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

namespace Apt.Nop.Plugin.Misc.Booster.Filters
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
        internal sealed class ProductDetailsFilter(
      BoosterSettings settings,
      IStaticCacheManager cacheManager,
      IStoreContext storeContext,
      IWorkContext workContext,
      IPermissionService permissionService,
      INopHtmlHelper nopHtmlHelper,
      IUrlHelperFactory urlHelperFactory,
      IWebHelper webHelper,
      ICustomerService customerService,
      IRecentlyViewedProductsService recentlyViewedProductsService)
      : BasePageFilter<ProductDetailsModel>(
          settings, cacheManager, storeContext, workContext,
          permissionService, nopHtmlHelper, urlHelperFactory, webHelper)
        {
            protected override string Controller => "Product";
            protected override string Action => "ProductDetails";

            protected override string CacheKeyFormat => BoosterConstants.PDP_CACHE_KEY_FORMAT;
            protected override int CacheLengthMinutes => settings.ProductDetailsPageCacheLengthMinutes;

            protected override bool TryGetId(ActionExecutingContext ctx, out int id)
            {
                if (ctx.ActionArguments.TryGetValue("productId", out var val) && val != null)
                {
                    id = Convert.ToInt32(val);
                    return true;
                }
                id = 0;
                return false;
            }

            protected override bool ShouldSkip(ActionExecutingContext ctx)
            {
                return ctx.ActionArguments.TryGetValue("updatecartitemid", out var v)
                       && Convert.ToInt32(v ?? "0") > 0;
            }

            protected override async Task OnCacheHitAsync(ActionExecutingContext ctx, int id)
            {
                await recentlyViewedProductsService.AddProductToRecentlyViewedListAsync(id);
            }

            protected override async Task AddEditLinkAsync(IUrlHelper urlHelper, int id)
            {
                if (!await permissionService.AuthorizeAsync(StandardPermission.Security.ACCESS_ADMIN_PANEL) ||
                    !await permissionService.AuthorizeAsync(StandardPermission.Catalog.PRODUCTS_CREATE_EDIT_DELETE))
                {
                    return;
                }

                nopHtmlHelper.AddEditPageUrl(urlHelper.Action("Edit", "Product",
                    new { id, area = AreaNames.ADMIN }));

            }

            protected override async Task<bool> CanCacheAsync(ActionExecutedContext ctx)
            {
                var customer = await workContext.GetCurrentCustomerAsync();
                var codes = await customerService.ParseAppliedDiscountCouponCodesAsync(customer);
                if (codes.Any())
                { return false; }

                if (webHelper.QueryString<string>("updatecartitemid") != null)
                    return false;

                return true;
            }

            protected override int GetModelId(ProductDetailsModel model) => model.Id;
        }
    }
}