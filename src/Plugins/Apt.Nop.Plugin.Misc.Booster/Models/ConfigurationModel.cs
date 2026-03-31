using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Apt.Nop.Plugin.Misc.Booster.Models;
public class ConfigurationModel
{
    [NopResourceDisplayName("Aperture.Plugins.Misc.Booster.Fields.CategoryPageCacheLengthMinutes")]
    public int CategoryPageCacheLengthMinutes { get; set; }

    [NopResourceDisplayName("Aperture.Plugins.Misc.Booster.Fields.ProductDetailsPageCacheLengthMinutes")]
    public int ProductDetailsPageCacheLengthMinutes { get; set; }

    [NopResourceDisplayName("Aperture.Plugins.Misc.Booster.Fields.ManufacturerPageCacheLengthMinutes")]
    public int ManufacturerPageCacheLengthMinutes { get; set; }


    [NopResourceDisplayName("Aperture.Plugins.Misc.Booster.Fields.Enabled")]
    public bool Enabled { get; set; }

    [NopResourceDisplayName("Aperture.Plugins.Misc.Booster.Fields.PageModifyingCustomerRoleIds")]
    public IList<int> PageModifyingCustomerRoleIds { get; set; }





    public bool CategoryPageCacheLengthMinutes_OverrideForStore { get; set; }
    public bool ProductDetailsPageCacheLengthMinutes_OverrideForStore { get; set; }
    public bool PageModifyingCustomerRoleIds_OverrideForStore { get; set; }
    public bool ManufacturerPageCacheLengthMinutes_OverrideForStore { get; set; }
    public bool Enabled_OverrideForStore { get; set; }

    public IList<SelectListItem> AvailableCustomerRoles { get; set; }



    public int ActiveStoreScopeConfiguration { get; set; }
}