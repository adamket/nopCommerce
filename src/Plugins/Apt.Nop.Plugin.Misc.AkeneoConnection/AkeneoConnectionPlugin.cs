using Nop.Core;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Orders;
using Nop.Services.Plugins;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection;

/// <summary>
/// CheckMoneyOrder payment processor
/// </summary>
public class AkeneoConnectionPlugin : BasePlugin, IMiscPlugin
{
    #region Fields

    protected readonly AkeneoConnectionSettings _checkMoneyOrderPaymentSettings;
    protected readonly ILocalizationService _localizationService;
    protected readonly IOrderTotalCalculationService _orderTotalCalculationService;
    protected readonly ISettingService _settingService;
    protected readonly IShoppingCartService _shoppingCartService;
    protected readonly IWebHelper _webHelper;

    #endregion

    #region Ctor

    public AkeneoConnectionPlugin(AkeneoConnectionSettings checkMoneyOrderPaymentSettings,
        ILocalizationService localizationService,
        IOrderTotalCalculationService orderTotalCalculationService,
        ISettingService settingService,
        IShoppingCartService shoppingCartService,
        IWebHelper webHelper)
    {
        _checkMoneyOrderPaymentSettings = checkMoneyOrderPaymentSettings;
        _localizationService = localizationService;
        _orderTotalCalculationService = orderTotalCalculationService;
        _settingService = settingService;
        _shoppingCartService = shoppingCartService;
        _webHelper = webHelper;
    }

    #endregion

    #region Methods

   

    /// <summary>
    /// Gets a configuration page URL
    /// </summary>
    public override string GetConfigurationPageUrl()
    {
        return $"{_webHelper.GetStoreLocation()}Admin/AkeneoConnectionConfiguration/Configure";
    }

  

    /// <summary>
    /// Install the plugin
    /// </summary>
    /// <returns>A task that represents the asynchronous operation</returns>
    public override async Task InstallAsync()
    {
        //settings
        var settings = new AkeneoConnectionSettings
        {
       //     DescriptionText = "<p>Mail Personal or Business Check, Cashier's Check or money order to:</p><p><br /><b>COMPANY NAME</b> <br /><b>your address here,</b> <br /><b>New York, NY 10001 </b> <br /><b>USA</b></p><p>Notice that if you pay by Personal or Business Check, your order may be held for up to 10 days after we receive your check to allow enough time for the check to clear.  If you want us to ship faster upon receipt of your payment, then we recommend your send a money order or Cashier's check.</p><p>P.S. You can edit this text from admin panel.</p>"
        };
        await _settingService.SaveSettingAsync(settings);

        //locales
        await _localizationService.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>
        {
            //["Plugins.Payment.CheckMoneyOrder.AdditionalFee"] = "Additional fee",
            //["Plugins.Payment.CheckMoneyOrder.AdditionalFee.Hint"] = "The additional fee.",
            //["Plugins.Payment.CheckMoneyOrder.AdditionalFeePercentage"] = "Additional fee. Use percentage",
            //["Plugins.Payment.CheckMoneyOrder.AdditionalFeePercentage.Hint"] = "Determines whether to apply a percentage additional fee to the order total. If not enabled, a fixed value is used.",
            //["Plugins.Payment.CheckMoneyOrder.DescriptionText"] = "Description",
            //["Plugins.Payment.CheckMoneyOrder.DescriptionText.Hint"] = "Enter info that will be shown to customers during checkout",
            //["Plugins.Payment.CheckMoneyOrder.PaymentMethodDescription"] = "Pay by cheque or money order",
            //["Plugins.Payment.CheckMoneyOrder.ShippableProductRequired"] = "Shippable product required",
            //["Plugins.Payment.CheckMoneyOrder.ShippableProductRequired.Hint"] = "An option indicating whether shippable products are required in order to display this payment method during checkout."
        });

        await base.InstallAsync();
    }

    /// <summary>
    /// Uninstall the plugin
    /// </summary>
    /// <returns>A task that represents the asynchronous operation</returns>
    public override async Task UninstallAsync()
    {
        //settings
        await _settingService.DeleteSettingAsync<AkeneoConnectionSettings>();

        //locales
       // await _localizationService.DeleteLocaleResourcesAsync("Plugins.Payment.CheckMoneyOrder");

        await base.UninstallAsync();
    }

    #endregion


}