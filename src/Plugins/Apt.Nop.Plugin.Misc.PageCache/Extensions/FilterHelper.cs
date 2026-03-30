using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Nop.Core;
using Nop.Core.Infrastructure;

namespace Apt.Nop.Plugin.Misc.PageCache.Extensions
{
    public static partial class FilterHelper
    {


        public static bool AllowFilter(this FilterContext filterContext, string controllerName, string actionName, PageCacheSettings pageCacheSettings = null)
        {
            pageCacheSettings ??= EngineContext.Current.Resolve<PageCacheSettings>();
            if (!pageCacheSettings.Enabled)
                return false;

            var isAction = filterContext.IsAction(controllerName, actionName);
            if (!isAction)
                return false;

            return true;
        }


        public static bool IsAction(this FilterContext context, IList<KeyValuePair<string, string>> controllerAndActionPairs, bool isAdmin = false, IWebHelper webHelper = null)
        {
            foreach (var kvp in controllerAndActionPairs)
            {
                var controllerName = kvp.Key;
                var actionName = kvp.Value;

                var isAction = context.IsAction(controllerName, actionName, isAdmin, webHelper);
                if (isAction)
                    return true;
            }

            return false;
        }

        public static bool IsAction(this FilterContext context, string controllerName, string actionName, bool isAdmin = false, IWebHelper webHelper = null)
        {
            var contextControllerName = (string)context.RouteData.Values["controller"];
            var contextActionName = (string)context.RouteData.Values["action"];
            var allowFilterToExecute = contextControllerName.Equals(controllerName, StringComparison.CurrentCultureIgnoreCase) && contextActionName.Equals(actionName, StringComparison.CurrentCultureIgnoreCase);
            return allowFilterToExecute;
        }


        public static bool IsController(this FilterContext context, string controllerName, bool isAdmin = false, IWebHelper webHelper = null)
        {
            var contextControllerName = (string)context.RouteData.Values["controller"];

            var allowFilterToExecute =
                contextControllerName.Equals(controllerName, StringComparison.CurrentCultureIgnoreCase);
            return allowFilterToExecute;
        }

        public static bool IsAjaxRequest(this FilterContext context)
        {
            if (context.HttpContext.Request == null)
                return false;

            var isAjaxRequest = context.HttpContext.Request?.Headers["X-Requested-With"].ToString() == "XMLHttpRequest";
            return isAjaxRequest;
        }


        public static T GetModel<T>(this IActionResult actionResult)
        {
            return ModelFromActionResult<T>(actionResult);
        }

        public static int GetEntityId(this ActionExecutingContext context)
        {
            var id = Convert.ToInt32(context.HttpContext.GetRouteData().Values["id"]);
            return id;
        }


        public static string GetValueFromActionParameters(this ActionExecutingContext filterContext, string key)
        {
            var value = filterContext.ActionArguments.ContainsKey(key)
                  ? filterContext.ActionArguments[key].ToString()
                  : filterContext.HttpContext.Request.Form.Keys.Contains(key)
                      ? filterContext.HttpContext.Request.Form[key].ToString()
                      : null;

            return value;
        }

        public static string GetViewName(this IActionResult actionResult)
        {
            if (actionResult is ViewResult @base)
                return @base.ViewName;

            return string.Empty;
        }

        public static bool IsPostback(this FilterContext filterContext)
        {
            var isPostBack = filterContext?.HttpContext?.Request?.Method == "POST";
            return isPostBack;
        }

        public static void SetView(this ResultExecutingContext filterContext, string viewPath)
        {
            if (filterContext.Result is ViewResult @base)
                @base.ViewName = viewPath;
        }

        public static T ModelFromActionResult<T>(IActionResult actionResult)
        {
            object model = null;
            if (actionResult is ViewResult @base)
            {
                var viewResult = @base;
                model = viewResult.Model;
            }
            else if (actionResult is ContentResult)
            {
                var @contentBase = (ContentResult)actionResult;
                model = @contentBase.Content;
            }
            else
            {
                return default;
            }

            T typedModel;
            try
            {
                typedModel = (T)model;
            }
            catch
            {
                return default;
            }

            return typedModel;
        }


        public static T ModelFromActionParameter<T>(object o)
        {
            T typedModel;
            try
            {
                typedModel = (T)o;
            }
            catch
            {
                return default;
            }
            return typedModel;
        }
    }
}
