using Apt.Nop.Plugin.Misc.AkeneoConnection.ScheduleTasks;
using Nop.Core;
using Nop.Core.Domain.ScheduleTasks;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Orders;
using Nop.Services.Plugins;
using Nop.Services.ScheduleTasks;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection;

public class AkeneoConnectionPlugin(
    ILocalizationService localizationService,
    IOrderTotalCalculationService orderTotalCalculationService,
    ISettingService settingService,
    IShoppingCartService shoppingCartService,
    IWebHelper webHelper,
    IScheduleTaskService scheduleTaskService)
    : BasePlugin, IMiscPlugin
{
    #region Fields



    #endregion

    #region Ctor

    #endregion

    #region Methods

   

    /// <summary>
    /// Gets a configuration page URL
    /// </summary>
    public override string GetConfigurationPageUrl()
    {
        return $"{webHelper.GetStoreLocation()}admin/akeneo-connection/configure";
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
     
        };

        var taskType = typeof(AkeneoProductImportScheduleTask).FullName
                       ?? throw new InvalidOperationException("Unable to resolve schedule task type.");

        var revisionCleanupTask = await scheduleTaskService.GetTaskByTypeAsync(taskType);
        if (revisionCleanupTask == null)
        {
            var cleanupTask = new ScheduleTask
            {
                Enabled = true,
                Seconds = 60 * 60 * 24, 
                Type = taskType,
                Name = "Akeneo Connection - Product Sync",

            };
            await scheduleTaskService.InsertTaskAsync(cleanupTask);
        }
        await settingService.SaveSettingAsync(settings);

        //locales
        await localizationService.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>
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
        await settingService.DeleteSettingAsync<AkeneoConnectionSettings>();

        var taskType = typeof(AkeneoProductImportScheduleTask).FullName;
        var task = await scheduleTaskService.GetTaskByTypeAsync(taskType);

        if (task != null)
            await scheduleTaskService.DeleteTaskAsync(task);

        //locales
        // await _localizationService.DeleteLocaleResourcesAsync("Plugins.Payment.CheckMoneyOrder");

        await base.UninstallAsync();
    }

    #endregion


}