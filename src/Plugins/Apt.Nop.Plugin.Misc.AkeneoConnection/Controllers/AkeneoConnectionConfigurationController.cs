using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Core;
using Nop.Services;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Plugins;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Controllers;

[AuthorizeAdmin]
[Area(AreaNames.ADMIN)]
[AutoValidateAntiforgeryToken]
public class AkeneoConnectionConfigurationController(
    IAkeneoApiClient akeneoApiClient,
    ILanguageService languageService,
    ILocalizationService localizationService,
    INotificationService notificationService,
    IPermissionService permissionService,
    ISettingService settingService,
    IStoreContext storeContext)
    : BasePluginController
{


    #region Ctor

    #endregion

    #region Utilities

    private async Task PrepareSyncContextOptionsAsync(AkeneoConfigurationModel model)
    {
        model.AvailableChannelCodes = new List<SelectListItem>();
        model.AvailableLocaleCodes = new List<SelectListItem>();
        model.AvailableCurrencyCodes = new List<SelectListItem>();

        IReadOnlyList<AkeneoChannelDefinition> channels;

        try
        {
            channels = await akeneoApiClient.GetChannelsAsync();
        }
        catch (Exception ex)
        {
            AddEmptyOption(model.AvailableChannelCodes, "Unable to load Akeneo channels");
            AddEmptyOption(model.AvailableLocaleCodes, "Unable to load Akeneo locales");
            AddEmptyOption(model.AvailableCurrencyCodes, "Unable to load Akeneo currencies");
            notificationService.WarningNotification($"Unable to load Akeneo sync context options. {ex.Message}");
            return;
        }

        var validChannels = channels
            .Where(channel => !string.IsNullOrWhiteSpace(channel.Code))
            .OrderBy(channel => channel.Code)
            .ToList();

        if (!validChannels.Any())
        {
            AddEmptyOption(model.AvailableChannelCodes, "No Akeneo channels available");
            AddEmptyOption(model.AvailableLocaleCodes, "No Akeneo locales available");
            AddEmptyOption(model.AvailableCurrencyCodes, "No Akeneo currencies available");
            notificationService.WarningNotification("No Akeneo channels were found. Verify your Akeneo connection and channel configuration.");
            return;
        }


        model.AvailableUnmappedAkeneoAttributeBehaviors = (await UnmappedAkeneoAttributeBehavior.Ignore.ToSelectListAsync(false)).ToList();

        model.DefaultChannelCode = ResolveSelectedCode(
            model.DefaultChannelCode,
            validChannels.Select(channel => channel.Code));

        foreach (var channel in validChannels)
        {
            model.AvailableChannelCodes.Add(new SelectListItem
            {
                Text = GetChannelDisplayName(channel),
                Value = channel.Code,
                Selected = string.Equals(channel.Code, model.DefaultChannelCode, StringComparison.OrdinalIgnoreCase)
            });
        }

        var selectedChannel = validChannels.First(channel =>
            string.Equals(channel.Code, model.DefaultChannelCode, StringComparison.OrdinalIgnoreCase));

        var localeCodes = NormalizeCodes(selectedChannel.Locales);
        if (!localeCodes.Any())
            localeCodes = NormalizeCodes(validChannels.SelectMany(c => c.Locales ?? Enumerable.Empty<string>()));

        model.DefaultLocaleCode = PrepareCodeOptions(
            model.AvailableLocaleCodes, localeCodes, model.DefaultLocaleCode, "No Akeneo locales available");

        var currencyCodes = NormalizeCodes(selectedChannel.Currencies);
        if (!currencyCodes.Any())
            currencyCodes = NormalizeCodes(validChannels.SelectMany(c => c.Currencies ?? Enumerable.Empty<string>()));

        model.DefaultCurrencyCode = PrepareCodeOptions(
            model.AvailableCurrencyCodes, currencyCodes, model.DefaultCurrencyCode, "No Akeneo currencies available");
    }

    private static string PrepareCodeOptions(
        IList<SelectListItem> options, IEnumerable<string> codes, string selectedCode, string emptyText)
    {
        options.Clear();
        var normalizedCodes = NormalizeCodes(codes);

        if (!normalizedCodes.Any())
        {
            AddEmptyOption(options, emptyText);
            return string.Empty;
        }

        var resolvedSelectedCode = ResolveSelectedCode(selectedCode, normalizedCodes);

        foreach (var code in normalizedCodes)
        {
            options.Add(new SelectListItem
            {
                Text = code,
                Value = code,
                Selected = string.Equals(code, resolvedSelectedCode, StringComparison.OrdinalIgnoreCase)
            });
        }

        return resolvedSelectedCode;
    }

    private static List<string> NormalizeCodes(IEnumerable<string> codes)
    {
        return (codes ?? [])
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code)
            .ToList();
    }

    private static string ResolveSelectedCode(string selectedCode, IEnumerable<string> validCodes)
    {
        var codes = NormalizeCodes(validCodes);
        if (!codes.Any())
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(selectedCode))
        {
            var match = codes.FirstOrDefault(code =>
                string.Equals(code, selectedCode, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match))
                return match;
        }

        return codes[0];
    }

    private static void AddEmptyOption(IList<SelectListItem> options, string text) =>
        options.Add(new SelectListItem { Text = text, Value = "" });

    private static string GetChannelDisplayName(AkeneoChannelDefinition channel)
    {
        var label = GetBestLabel(channel.Labels);
        if (string.IsNullOrWhiteSpace(label) ||
            string.Equals(label, channel.Code, StringComparison.OrdinalIgnoreCase))
        {
            return channel.Code;
        }

        return $"{label} ({channel.Code})";
    }

    private static string GetBestLabel(IDictionary<string, string> labels)
    {
        if (labels == null || !labels.Any())
            return string.Empty;

        if (labels.TryGetValue("en_US", out var englishLabel) && !string.IsNullOrWhiteSpace(englishLabel))
            return englishLabel;

        return labels.Values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    #endregion

    #region Methods

    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public async Task<IActionResult> Configure()
    {
        //load settings for a chosen store scope
        var storeScope = await storeContext.GetActiveStoreScopeConfigurationAsync();
        var akeneoConnectionSettings = await settingService.LoadSettingAsync<AkeneoConnectionSettings>(storeScope);

        var model = new AkeneoConfigurationModel
        {
            ActiveStoreScopeConfiguration = storeScope,
            AkeneoConnectionBaseUrl = akeneoConnectionSettings.AkeneoConnectionBaseUrl,
            AkeneoConnectionClientId = akeneoConnectionSettings.AkeneoConnectionClientId,
            AkeneoConnectionClientSecret = akeneoConnectionSettings.AkeneoConnectionClientSecret,
            AkeneoConnectionUsername = akeneoConnectionSettings.AkeneoConnectionUsername,
            AkeneoConnectionPassword = akeneoConnectionSettings.AkeneoConnectionPassword,
            DefaultChannelCode = akeneoConnectionSettings.DefaultChannelCode,
            DefaultLocaleCode = akeneoConnectionSettings.DefaultLocaleCode,
            DefaultCurrencyCode = akeneoConnectionSettings.DefaultCurrencyCode
        };

        if (storeScope > 0)
        {
            model.AkeneoConnectionBaseUrl_OverrideForStore = await settingService.SettingExistsAsync(akeneoConnectionSettings, x => x.AkeneoConnectionBaseUrl, storeScope);
            model.AkeneoConnectionClientId_OverrideForStore = await settingService.SettingExistsAsync(akeneoConnectionSettings, x => x.AkeneoConnectionClientId, storeScope);
            model.AkeneoConnectionClientSecret_OverrideForStore = await settingService.SettingExistsAsync(akeneoConnectionSettings, x => x.AkeneoConnectionClientSecret, storeScope);
            model.AkeneoConnectionUsername_OverrideForStore = await settingService.SettingExistsAsync(akeneoConnectionSettings, x => x.AkeneoConnectionUsername, storeScope);
            model.AkeneoConnectionPassword_OverrideForStore = await settingService.SettingExistsAsync(akeneoConnectionSettings, x => x.AkeneoConnectionPassword, storeScope);
            model.DefaultChannelCode_OverrideForStore = await settingService.SettingExistsAsync(akeneoConnectionSettings, x => x.DefaultChannelCode, storeScope);
            model.DefaultLocaleCode_OverrideForStore = await settingService.SettingExistsAsync(akeneoConnectionSettings, x => x.DefaultLocaleCode, storeScope);
            model.DefaultCurrencyCode_OverrideForStore = await settingService.SettingExistsAsync(akeneoConnectionSettings, x => x.DefaultCurrencyCode, storeScope);
        }

        await PrepareSyncContextOptionsAsync(model);

        return View("~/Plugins/Apt.Misc.AkeneoConnection/Views/Configure.cshtml", model);
    }

    [HttpPost]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public async Task<IActionResult> Configure(AkeneoConfigurationModel model)
    {
        if (!ModelState.IsValid)
            return await Configure();

        //load settings for a chosen store scope
        var storeScope = await storeContext.GetActiveStoreScopeConfigurationAsync();
        var akeneoConnectionSettings = await settingService.LoadSettingAsync<AkeneoConnectionSettings>(storeScope);

        //save settings
        akeneoConnectionSettings.AkeneoConnectionBaseUrl = model.AkeneoConnectionBaseUrl;
        akeneoConnectionSettings.AkeneoConnectionClientId = model.AkeneoConnectionClientId;
        akeneoConnectionSettings.AkeneoConnectionClientSecret = model.AkeneoConnectionClientSecret;
        akeneoConnectionSettings.AkeneoConnectionUsername = model.AkeneoConnectionUsername;
        akeneoConnectionSettings.AkeneoConnectionPassword = model.AkeneoConnectionPassword;
        akeneoConnectionSettings.DefaultLocaleCode = model.DefaultLocaleCode;
        akeneoConnectionSettings.DefaultChannelCode = model.DefaultChannelCode;
        akeneoConnectionSettings.DefaultCurrencyCode = model.DefaultCurrencyCode;

        /* We do not clear cache after each setting update.
         * This behavior can increase performance because cached settings will not be cleared
         * and loaded from database after each update */
        await settingService.SaveSettingOverridablePerStoreAsync(akeneoConnectionSettings, x => x.AkeneoConnectionBaseUrl, model.AkeneoConnectionBaseUrl_OverrideForStore, storeScope, false);
        await settingService.SaveSettingOverridablePerStoreAsync(akeneoConnectionSettings, x => x.AkeneoConnectionClientId, model.AkeneoConnectionClientId_OverrideForStore, storeScope, false);
        await settingService.SaveSettingOverridablePerStoreAsync(akeneoConnectionSettings, x => x.AkeneoConnectionClientSecret, model.AkeneoConnectionClientSecret_OverrideForStore, storeScope, false);
        await settingService.SaveSettingOverridablePerStoreAsync(akeneoConnectionSettings, x => x.AkeneoConnectionUsername, model.AkeneoConnectionUsername_OverrideForStore, storeScope, false);
        await settingService.SaveSettingOverridablePerStoreAsync(akeneoConnectionSettings, x => x.AkeneoConnectionPassword, model.AkeneoConnectionPassword_OverrideForStore, storeScope, false);
        await settingService.SaveSettingOverridablePerStoreAsync(akeneoConnectionSettings, x => x.DefaultChannelCode, model.DefaultChannelCode_OverrideForStore, storeScope, false);
        await settingService.SaveSettingOverridablePerStoreAsync(akeneoConnectionSettings, x => x.DefaultLocaleCode, model.DefaultLocaleCode_OverrideForStore, storeScope, false);
        await settingService.SaveSettingOverridablePerStoreAsync(akeneoConnectionSettings, x => x.DefaultCurrencyCode, model.DefaultCurrencyCode_OverrideForStore, storeScope, false);

        //now clear settings cache
        await settingService.ClearCacheAsync();

        notificationService.SuccessNotification(await localizationService.GetResourceAsync("Admin.Plugins.Saved"));

        return await Configure();
    }

    #endregion
}