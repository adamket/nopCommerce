using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Razor;
using Nop.Core.Infrastructure;
using Nop.Web.Framework;
using Nop.Web.Framework.Themes;

namespace Apt.Nop.Plugin.Misc.NopCms.Infrastructure
{

    public class ViewLocationExpander : IViewLocationExpander
    {
        private const string _CustomViewPath = "CustomViewPath";
        private const string _CustomController = "CustomController";
      //  private static Regex componentDetector = new(@"components/topicblock/default", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        public void PopulateValues(ViewLocationExpanderContext context)
        {

            var componentMatch = context.ViewName.Contains("components/topicblock/default", StringComparison.CurrentCultureIgnoreCase);
            if (componentMatch)
            {
                // Will render Components/ComponentName as the new view name
                context.Values.Add(_CustomViewPath, context.ViewName);
                  //  $"{componentMatch.Groups[1].Value}/{componentMatch.Groups[2].Value}");
                context.Values.Add(_CustomController, context.ControllerName);
            }

        }

        public IEnumerable<string> ExpandViewLocations(ViewLocationExpanderContext context, IEnumerable<string> viewLocations)
        {
            if (context.Values.TryGetValue(_CustomViewPath, out var customViewComponent))
            {
                viewLocations = [
                    $"/Plugins/Misc.NopCms/Views/Shared/{customViewComponent}.cshtml"
                ];

                return viewLocations;
            }

            return viewLocations;
        }
    }
}