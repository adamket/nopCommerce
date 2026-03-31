namespace Apt.Nop.Plugin.Misc.Booster;
public static class BoosterConstants
{
    public const string PDP_CACHE_KEY_PREFIX = "apt.product-details-cache-page-{0}"; //productid
    public const string PDP_CACHE_KEY_FORMAT = PDP_CACHE_KEY_PREFIX + "-{1}-{2}"; //  {1} - storeId, {2} - customer roles

    public const string CATEGORY_PAGE_CACHE_KEY_PREFIX = "apt.category-page-cache-{0}"; // categoryId
    public const string CATEGORY_PAGE_CACHE_KEY_FORMAT = CATEGORY_PAGE_CACHE_KEY_PREFIX + "-{1}-{2}"; // {1} - storeId, {2} - customer roles

    public const string MANUFACTURER_PAGE_CACHE_KEY_PREFIX = "apt.manufacturer-page-cache-{0}"; // categoryId
    public const string MANUFACTURER_PAGE_CACHE_KEY_FORMAT = MANUFACTURER_PAGE_CACHE_KEY_PREFIX + "-{1}-{2}"; // {1} - storeId, {2} - customer roles
}
