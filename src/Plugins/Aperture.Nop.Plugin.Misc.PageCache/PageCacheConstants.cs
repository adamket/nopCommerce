using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aperture.Nop.Plugin.Misc.PageCache;
public static class PageCacheConstants
{
    public const string PDP_CACHE_KEY_PREFIX = "apt.product-details-cache-page-{0}"; //productid
    public const string PDP_CACHE_KEY_FORMAT = PDP_CACHE_KEY_PREFIX + "-{1}-{2}"; //  {1} - storeId, {2} - customer roles

    public const string CATEGORY_PAGE_CACHE_KEY_PREFIX = "apt.category-page-cache-{0}"; // categoryId
    public const string CATEGORY_PAGE_CACHE_KEY_FORMAT = CATEGORY_PAGE_CACHE_KEY_PREFIX + "-{1}-{2}"; // {1} - storeId, {2} - customer roles
}
