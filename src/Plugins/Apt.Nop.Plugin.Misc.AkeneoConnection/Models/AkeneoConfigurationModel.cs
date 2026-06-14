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

        
    
    [NopResourceDisplayName("Plugins.Misc.AkeneoConnection.Channel")]
    public string AkeneoConnectionChannel { get; set; }
    public bool AkeneoConnectionChannel_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AkeneoConnection.Locale")]
    public string AkeneoConnectionLocale { get; set; }
    public bool AkeneoConnectionLocale_OverrideForStore { get; set; }



    //[NopResourceDisplayName("Plugins.Payment.CheckMoneyOrder.DescriptionText")]
    //public string DescriptionText { get; set; }
    //public bool DescriptionText_OverrideForStore { get; set; }

    //[NopResourceDisplayName("Plugins.Payment.CheckMoneyOrder.AdditionalFee")]
    //public decimal AdditionalFee { get; set; }
    //public bool AdditionalFee_OverrideForStore { get; set; }

    //[NopResourceDisplayName("Plugins.Payment.CheckMoneyOrder.AdditionalFeePercentage")]
    //public bool AdditionalFeePercentage { get; set; }
    //public bool AdditionalFeePercentage_OverrideForStore { get; set; }

    //[NopResourceDisplayName("Plugins.Payment.CheckMoneyOrder.ShippableProductRequired")]
    //public bool ShippableProductRequired { get; set; }
    //public bool ShippableProductRequired_OverrideForStore { get; set; }

    //public IList<ConfigurationLocalizedModel> Locales { get; set; }

    #region Nested class

    //public class ConfigurationLocalizedModel : ILocalizedLocaleModel
    //{
    //    public int LanguageId { get; set; }

    //    [NopResourceDisplayName("Plugins.Payment.CheckMoneyOrder.DescriptionText")]
    //    public string DescriptionText { get; set; }
    //}

    #endregion

}