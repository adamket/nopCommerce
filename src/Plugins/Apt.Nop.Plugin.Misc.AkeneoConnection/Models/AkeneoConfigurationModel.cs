using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Models;

public record AkeneoConfigurationModel : BaseNopModel
{
    public int ActiveStoreScopeConfiguration { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AkeneoConnection.ClientId")]
    public string AkeneoConnectionClientId { get; set; }
    public bool AkeneoConnectionClientId_OverrideForStore { get; set; }
    [NopResourceDisplayName("Plugins.Misc.AkeneoConnection.ClientSecret")]
    public string AkeneoConnectionClientSecret { get; set; }
    public bool AkeneoConnectionClientSecret_OverrideForStore { get; set; }
    [NopResourceDisplayName("Plugins.Misc.AkeneoConnection.BaseUrl")]
    public string AkeneoConnectionBaseUrl { get; set; }
    public bool AkeneoConnectionBaseUrl_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AkeneoConnection.Username")]
    public string AkeneoConnectionUsername { get; set; }
    public bool AkeneoConnectionUsername_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AkeneoConnection.Password")]
    public string AkeneoConnectionPassword { get; set; }
    public bool AkeneoConnectionPassword_OverrideForStore { get; set; }


    [NopResourceDisplayName("Plugins.Misc.AkeneoConnection.DefaultSyncProfile")]
    public int? DefaultSyncProfileId{ get; set; }
    public bool DefaultSyncProfileId_OverrideForStore { get; set; }

    public List<SelectListItem> AvailableSyncProfiles { get; set; }
}