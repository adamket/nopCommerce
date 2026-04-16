using System.ComponentModel;
using System.Dynamic;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Nop.Core.Infrastructure;

namespace Apt.Nop.Plugin.Misc.OneTimePasscode.Extensions
{
    public static class OtpWebControllerExtensions
    {

        public static IActionResult Error(this Controller controller,
    string message,
    IHttpContextAccessor httpContextAccessor = null)
        {
            httpContextAccessor = httpContextAccessor ?? EngineContext.Current.Resolve<IHttpContextAccessor>();
            httpContextAccessor.HttpContext.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            return new ContentResult
            {
                Content = message
            };
        }

        public static IActionResult OtpJson(this Controller controller,
            bool success, string message = null, object data = null,
            IHttpContextAccessor httpContextAccessor = null)
        {
            httpContextAccessor = httpContextAccessor ?? EngineContext.Current.Resolve<IHttpContextAccessor>();
            httpContextAccessor.HttpContext.Response.StatusCode = (int)HttpStatusCode.OK;

            var dyn = ToDynamic(data ?? new object());
            dyn.success = success;

            if (!string.IsNullOrEmpty(message))
            {
                dyn.message = message;
            }

            return controller.Json(dyn);
        }


        public static IActionResult OtpJsonSuccess(this Controller controller,
            object data = null,
            IHttpContextAccessor httpContextAccessor = null)
        {
            return controller.OtpJson(true, data: data, httpContextAccessor: httpContextAccessor);
        }

        public static IActionResult OtpJsonError(this Controller controller,
            string message, object data = null,
            IHttpContextAccessor httpContextAccessor = null)
        {
            return controller.OtpJson(false, message, data, httpContextAccessor);
        }
        public static IActionResult OtpJsonError(this Controller controller,
            IList<string> errors,
            IHttpContextAccessor httpContextAccessor = null)
        {
            return controller.OtpJsonError(string.Join(", ", errors.Where(q => !string.IsNullOrEmpty(q)).ToList()), httpContextAccessor: httpContextAccessor);
        }

        private static dynamic ToDynamic(object value)
        {
            IDictionary<string, object> expando = new ExpandoObject();

            foreach (PropertyDescriptor property in TypeDescriptor.GetProperties(value.GetType()))
                expando.Add(property.Name, property.GetValue(value));

            return expando as ExpandoObject;
        }


        private static IDictionary<string, object> ToDictionary(this object value)
        {
            try
            {
                var dict =
                    JsonConvert.DeserializeObject<IDictionary<string, object>>(JsonConvert.SerializeObject(value));
                return dict;
            }
            catch
            {
                return null;
            }
        }

        public static IList<string> GetModelStateErrors(this Controller controller)
        {
            var messages = controller.ModelState.Values
                .SelectMany(x => x.Errors)
                .Select(x => x.ErrorMessage).ToList();

            return messages;
        }

        private static IDictionary<string, object> Merge(this IDictionary<string, object> dictA,
            IDictionary<string, object> dictB)
        {
            var result = dictA.Concat(dictB.Where(kvp => !dictA.ContainsKey(kvp.Key)))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            return result;

        }
    }
}
