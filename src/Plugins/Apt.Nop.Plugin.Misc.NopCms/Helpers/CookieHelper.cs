using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Nop.Core.Infrastructure;
using Nop.Services.Logging;

namespace Apt.Nop.Plugin.Misc.NopCms.Helpers
{
    public static class CookieHelper
    {
        public static string GetCookie(string cookieName, IHttpContextAccessor httpContextAccessor = null)
        {
            httpContextAccessor ??= EngineContext.Current.Resolve<IHttpContextAccessor>();
            return httpContextAccessor.HttpContext?.Request?.Cookies[cookieName];
        }

        public static T GetCookie<T>(string cookieName, IHttpContextAccessor httpContextAccessor = null)
        {
            httpContextAccessor ??= EngineContext.Current.Resolve<IHttpContextAccessor>();


            var jsonStr = httpContextAccessor.HttpContext?.Request?.Cookies[cookieName];
            if (jsonStr == null)
            {
                return default(T);
            }

            try
            {
                var obj = JsonConvert.DeserializeObject<T>(jsonStr);
                return obj;
            }
            catch (Exception e)
            {
                EngineContext.Current.Resolve<ILogger>().Error("Error deserializing cookie json.", e);
                return default(T);
            }
        }


        public static void RemoveCookie(string cookieName, IHttpContextAccessor httpContextAccessor = null)
        {
            SetCookie<string>(cookieName, null, null, false, httpContextAccessor);
        }




        public static void SetCookie<T>(string cookieName, T cookieValue, DateTime? expireDate = null, bool httpOnly = false, IHttpContextAccessor httpContextAccessor = null)
        {
            httpContextAccessor ??= EngineContext.Current.Resolve<IHttpContextAccessor>();
            if (httpContextAccessor?.HttpContext?.Response == null)
            {
                return;
            }

            if (httpContextAccessor.HttpContext?.Request?.Cookies[cookieName] != null)
            {
                httpContextAccessor.HttpContext.Response.Cookies.Delete(cookieName);
            }


            //if passed guid is empty set cookie as expired
            if (cookieValue == null)
            {
                expireDate = DateTime.Now.AddMonths(-1);
            }

            //set new cookie value
            var options = new CookieOptions
            {
                HttpOnly = httpOnly,
                Expires = expireDate
            };

            var value = string.Empty;
            try
            {
                value = JsonConvert.SerializeObject(cookieValue);
            }
            catch (Exception e)
            {
                ;
            }

            httpContextAccessor.HttpContext?.Response.Cookies.Append(cookieName, value, options);
        }
    }
}
