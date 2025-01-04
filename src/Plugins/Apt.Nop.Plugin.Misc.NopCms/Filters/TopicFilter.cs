using FluentMigrator.Runner.Generators.Snowflake;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Web.Controllers;

namespace Apt.Nop.Plugin.Misc.NopCms.Filters;

public class TopicFilter : ActionFilterAttribute
{
    private readonly IWorkContext _workContext;
    private readonly IStoreContext _storeContext;
    public TopicFilter(IWorkContext workContext, IStoreContext storeContext)
    {
        _workContext = workContext;
        _storeContext = storeContext;
    }

    public override void OnActionExecuted(ActionExecutedContext filterContext)
    {
        if (filterContext.Controller is not TopicController 
            || filterContext.ActionDescriptor is not ControllerActionDescriptor controllerActionDescriptor
            || filterContext.Result is not ViewResult result 
            || !"TopicDetails".Equals(controllerActionDescriptor.ActionName, StringComparison.CurrentCultureIgnoreCase))
        {
            return;
        }

        result.ViewName = "~/plugins/misc.nopcms/views/topic/TopicDetails.cshtml";
    }
}


