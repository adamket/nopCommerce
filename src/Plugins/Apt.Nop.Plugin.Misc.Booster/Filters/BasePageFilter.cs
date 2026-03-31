using Apt.Nop.Plugin.Misc.Booster.Extensions;
using Apt.Nop.Plugin.Misc.Booster.Types;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Nop.Core;
using Nop.Core.Caching;
using Nop.Services.Security;
using Nop.Web.Framework.UI;

namespace Apt.Nop.Plugin.Misc.Booster.Filters;
internal abstract class BasePageFilter<TModel>(
    BoosterSettings settings,
    IStaticCacheManager cacheManager,
    IStoreContext storeContext,
    IWorkContext workContext,
    IPermissionService permissionService,
    INopHtmlHelper nopHtmlHelper,
    IUrlHelperFactory urlHelperFactory,
    IWebHelper webHelper) : IAsyncActionFilter
    where TModel : class
{
    protected abstract string Controller { get; }
    protected abstract string Action { get; }
    protected abstract string CacheKeyFormat { get; }
    protected abstract int CacheLengthMinutes { get; }

    protected abstract bool TryGetId(ActionExecutingContext ctx, out int id);
    protected abstract bool ShouldSkip(ActionExecutingContext ctx);

    protected abstract Task AddEditLinkAsync(IUrlHelper urlHelper, int id);

    protected virtual Task OnCacheHitAsync(
        ActionExecutingContext ctx, int id) => Task.CompletedTask;

    public async Task OnActionExecutionAsync(
        ActionExecutingContext ctx, ActionExecutionDelegate next)
    {
        await OnExecutingAsync(ctx);
        if (ctx.Result == null)
        {
            var resultCtx = await next();
            await OnExecutedAsync(resultCtx);
        }
    }

    private async Task OnExecutingAsync(ActionExecutingContext ctx)
    {
        if (!ctx.AllowFilter(Controller, Action, settings)) return;
        if (ShouldSkip(ctx)) return;
        if (!TryGetId(ctx, out var id)) return;

        var customer = await workContext.GetCurrentCustomerAsync();
        var store = await storeContext.GetCurrentStoreAsync();
        var rolesStr = await customer.GetCustomerRoleIdsStrDescAsync();

        var cacheKey = new CacheKey(
            string.Format(CacheKeyFormat, id, store.Id, rolesStr))
        { CacheTime = int.MaxValue };

        var cached = await cacheManager
            .GetAsync(cacheKey, () => (ActionResultCacheItem<TModel>)null);

        if (cached == null) return;

        await OnCacheHitAsync(ctx, id);

        var urlHelper = urlHelperFactory.GetUrlHelper(ctx);
        await AddEditLinkAsync(urlHelper, id);

        ctx.Result = cached.GetResult<ViewResult>();
    }

    private async Task OnExecutedAsync(ActionExecutedContext ctx)
    {
        if (!ctx.AllowFilter(Controller, Action, settings)) return;
        if (ctx.HttpContext.Request.Query.Any()) return;

        var model = ctx.Result.GetModel<TModel>();
        if (model == null) return;

        if (!await CanCacheAsync(ctx)) return;

        var store = await storeContext.GetCurrentStoreAsync();
        var customer = await workContext.GetCurrentCustomerAsync();
        var rolesStr = await customer.GetCustomerRoleIdsStrDescAsync();
        var id = GetModelId(model);

        var cacheKey = new CacheKey(
            string.Format(CacheKeyFormat, id, store.Id, rolesStr))
        { CacheTime = CacheLengthMinutes };

        await cacheManager.SetAsync(cacheKey,
            new ActionResultCacheItem<TModel>(ctx.Result));
    }

    protected virtual Task<bool> CanCacheAsync(ActionExecutedContext ctx)
        => Task.FromResult(true);

    protected abstract int GetModelId(TModel model);
}