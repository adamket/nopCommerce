using Microsoft.AspNetCore.Mvc.Razor;
using Nop.Core.Infrastructure;
using Nop.Web.Framework;
using Nop.Web.Framework.Themes;

namespace Apt.Nop.Plugin.Misc.OneTimePasscode.Infrastructure
{
    /// <summary>
    /// Adds this plugin's Views folder to the Razor view location search list
    /// so view components and views inside the plugin can be resolved.
    /// Theme overrides are checked first so a theme can replace any view.
    /// </summary>
    public class PluginViewLocationExpander : IViewLocationExpander
    {
        private const string THEME_KEY = "nop.themename";
        protected const string HTTP_CONTEXT_THEME_CACHE_KEY = "http-context-theme-cache-key";

        public void PopulateValues(ViewLocationExpanderContext context)
        {
            //no need to add the themeable view locations at all as the administration should not be themeable anyway
            if (context.AreaName?.Equals(AreaNames.ADMIN) ?? false)
                return;

            var httpContext = context.ActionContext.HttpContext;
            if (!httpContext.Items.TryGetValue(HTTP_CONTEXT_THEME_CACHE_KEY, out var cachedThemeName))
            {
                cachedThemeName = EngineContext.Current.Resolve<IThemeContext>().GetWorkingThemeNameAsync().Result;
                httpContext.Items[HTTP_CONTEXT_THEME_CACHE_KEY] = cachedThemeName;
            }

            context.Values[THEME_KEY] = (string)cachedThemeName;
        }

        public IEnumerable<string> ExpandViewLocations(
            ViewLocationExpanderContext context,
            IEnumerable<string> viewLocations)
        {
            var pluginLocations = new List<string>();

            if (context.Values.TryGetValue(THEME_KEY, out var theme) && !string.IsNullOrEmpty(theme))
            {
                pluginLocations.Add($"/Themes/{theme}{OtpConstants.PathToPlugin}/Views/{{1}}/{{0}}.cshtml");
                pluginLocations.Add($"/Themes/{theme}{OtpConstants.PathToPlugin}/Views/Shared/{{0}}.cshtml");
            }

            pluginLocations.Add($"{OtpConstants.PathToPlugin}/Views/{{1}}/{{0}}.cshtml");
            pluginLocations.Add($"{OtpConstants.PathToPlugin}/Views/Shared/{{0}}.cshtml");

            return pluginLocations.Concat(viewLocations);
        }
    }
}