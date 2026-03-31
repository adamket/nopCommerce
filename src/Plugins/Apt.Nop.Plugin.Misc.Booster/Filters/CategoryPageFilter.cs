using Apt.Nop.Plugin.Misc.Booster.Extensions;
using Apt.Nop.Plugin.Misc.Booster.Types;
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

namespace Apt.Nop.Plugin.Misc.Booster.Filters
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

        internal sealed class CategoryFilter(BoosterSettings settings,
            IStaticCacheManager cacheManager,
            IStoreContext storeContext,
            IWorkContext workContext,
            IPermissionService permissionService,
            INopHtmlHelper nopHtmlHelper,
            IUrlHelperFactory urlHelperFactory,
            IWebHelper webHelper,
            IGenericAttributeService genericAttributeService) : BrowsePageFilter<CategoryModel>(settings, cacheManager, storeContext, workContext, permissionService, nopHtmlHelper, urlHelperFactory, webHelper, genericAttributeService)
        {
            protected override string Controller => "Catalog";
            protected override string Action => "Category";
            protected override string CacheKeyFormat => BoosterConstants.CATEGORY_PAGE_CACHE_KEY_FORMAT;
            protected override int CacheLengthMinutes => settings.CategoryPageCacheLengthMinutes;

            protected override bool TryGetId(ActionExecutingContext ctx, out int id)
            {
                if (ctx.ActionArguments.TryGetValue("categoryId", out var val) && val != null)
                {
                    id = Convert.ToInt32(val);
                    return true;
                }
                id = 0;
                return false;
            }

            protected override bool ShouldSkip(ActionExecutingContext ctx) =>
                ctx.HttpContext.Request.Query.Any() ||
                ctx.HttpContext.Request.Query.TryGetValue(
                    NopDiscountDefaults.DiscountCouponQueryParameter, out var c)
                && !StringValues.IsNullOrEmpty(c);

            protected override async Task AddEditLinkAsync(IUrlHelper urlHelper, int id)
            {
                if (!await permissionService.AuthorizeAsync(StandardPermission.Security.ACCESS_ADMIN_PANEL) ||
                    !await permissionService.AuthorizeAsync(StandardPermission.Catalog.CATEGORIES_CREATE_EDIT_DELETE))
                {
                    return;
                }

                nopHtmlHelper.AddEditPageUrl(urlHelper.Action("Edit", "Category",
                    new { id, area = AreaNames.ADMIN }));

            }

            protected override int GetModelId(CategoryModel model) => model.Id;
        }



    }
}