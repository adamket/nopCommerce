using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Primitives;
using Nop.Core;
using Nop.Core.Caching;
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
    public sealed class ManufacturerActionAttribute : TypeFilterAttribute
    {
        #region Ctor

        /// <summary>
        /// Create instance of the filter attribute
        /// </summary>
        /// <param name="ignore">Whether to ignore the execution of filter actions</param>
        public ManufacturerActionAttribute() : base(typeof(ManufacturerFilter))
        {

        }

        #endregion
        /// <summary>
        /// Represents a filter that confirms access to closed store
        /// </summary>
        // ManufacturerFilter.cs
        internal sealed class ManufacturerFilter(
            BoosterSettings settings,
            IStaticCacheManager cacheManager,
            IStoreContext storeContext,
            IWorkContext workContext,
            IPermissionService permissionService,
            INopHtmlHelper nopHtmlHelper,
            IUrlHelperFactory urlHelperFactory,
            IWebHelper webHelper,
            IGenericAttributeService genericAttributeService)
            : BrowsePageFilter<ManufacturerModel>(
                settings, cacheManager, storeContext, workContext,
                permissionService, nopHtmlHelper, urlHelperFactory,
                webHelper, genericAttributeService)
        {
            protected override string Controller => "Catalog";
            protected override string Action => "Manufacturer";

            protected override string CacheKeyFormat => BoosterConstants.MANUFACTURER_PAGE_CACHE_KEY_FORMAT;
            protected override int CacheLengthMinutes => settings.ManufacturerPageCacheLengthMinutes;

            protected override bool TryGetId(ActionExecutingContext ctx, out int id)
            {
                if (ctx.ActionArguments.TryGetValue("manufacturerId", out var val) && val != null)
                {
                    id = Convert.ToInt32(val);
                    return true;
                }
                id = 0;
                return false;
            }

            protected override bool ShouldSkip(ActionExecutingContext ctx)
            {
                if (ctx.HttpContext.Request.Query.Any())
                    return true;

                return ctx.HttpContext.Request.Query.TryGetValue(
                           NopDiscountDefaults.DiscountCouponQueryParameter,
                           out var codes)
                       && !StringValues.IsNullOrEmpty(codes);
            }

            protected override async Task AddEditLinkAsync(IUrlHelper urlHelper, int id)
            {
                if (await permissionService.AuthorizeAsync(StandardPermission.Security.ACCESS_ADMIN_PANEL) &&
                    await permissionService.AuthorizeAsync(StandardPermission.Catalog.MANUFACTURER_CREATE_EDIT_DELETE))
                {
                    nopHtmlHelper.AddEditPageUrl(urlHelper.Action("Edit", "Manufacturer",
                        new { id, area = AreaNames.ADMIN }));
                }
            }

            protected override int GetModelId(ManufacturerModel model) => model.Id;
        }
    }

}