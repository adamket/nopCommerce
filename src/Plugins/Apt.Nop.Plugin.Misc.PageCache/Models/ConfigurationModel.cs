using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Apt.Nop.Plugin.Misc.PageCache.Models;
public class ConfigurationModel
{
    [NopResourceDisplayName("Aperture.Plugins.Misc.PageCache.Fields.CategoryPageCacheLengthMinutes")]
    public int CategoryPageCacheLengthMinutes { get; set; }

    [NopResourceDisplayName("Aperture.Plugins.Misc.PageCache.Fields.ProductDetailsPageCacheLengthMinutes")]
    public int ProductDetailsPageCacheLengthMinutes { get; set; }

    [NopResourceDisplayName("Aperture.Plugins.Misc.PageCache.Fields.ManufacturerPageCacheLengthMinutes")]
    public int ManufacturerPageCacheLengthMinutes { get; set; }


    [NopResourceDisplayName("Aperture.Plugins.Misc.PageCache.Fields.Enabled")]
    public bool Enabled { get; set; }

    [NopResourceDisplayName("Aperture.Plugins.Misc.PageCache.Fields.PageModifyingCustomerRoleIds")]
    public IList<int> PageModifyingCustomerRoleIds { get; set; }





    public bool CategoryPageCacheLengthMinutes_OverrideForStore { get; set; }
    public bool ProductDetailsPageCacheLengthMinutes_OverrideForStore { get; set; }
    public bool PageModifyingCustomerRoleIds_OverrideForStore { get; set; }
    public bool ManufacturerPageCacheLengthMinutes_OverrideForStore { get; set; }
    public bool Enabled_OverrideForStore { get; set; }

    public IList<SelectListItem> AvailableCustomerRoles { get; set; }



    public int ActiveStoreScopeConfiguration { get; set; }
}