using Aperture.Nop.Plugin.Misc.PageCache.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Aperture.Nop.Plugin.Misc.PageCache.Types
{
    public class ActionResultCacheItem<U>
    {
        public ActionResultCacheItem()
        {
        }

        public ActionResultCacheItem(IActionResult result)
        {
            Model = result.GetModel<U>();
            ViewName = result.GetViewName();
        }

        public string ViewName { get; set; }
        public U Model { get; set; }

        public T GetResult<T>() where T : ViewResult, new()
        {
            var viewDataDictionary =
                new ViewDataDictionary<U>(new Microsoft.AspNetCore.Mvc.ModelBinding.EmptyModelMetadataProvider(),
                        new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary())
                    { };
            viewDataDictionary.Model = Model;

            var viewResult = new T
            {
                ViewData = viewDataDictionary,
                ViewName = ViewName
            };

            return viewResult;
        }
    }

}