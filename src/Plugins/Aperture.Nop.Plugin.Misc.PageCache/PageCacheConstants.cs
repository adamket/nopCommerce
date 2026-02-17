using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aperture.Nop.Plugin.Misc.PageCache;
public static class PageCacheConstants
{
    public const string PDP_CACHE_KEY_FORMAT = "product-details-cache-page-{0}-{1}-{2}"; // {0} - productId, {1} - storeId, {2} - customer roles
    public const string CATEGORY_CACHE_KEY_FORMAT = "category-page-cache-{0}-{1}-{2}"; // {0} - categoryId, {1} - storeId, {2} - customer roles
}
