using System.Text;
using Apt.Nop.Plugin.Misc.OneTimePasscode.Extensions;
using Apt.Nop.Plugin.Misc.OneTimePasscode.Models;
using Apt.Nop.Plugin.Misc.OneTimePasscode.Services;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Core.Events;
using Nop.Services.Authentication;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Messages;
using Nop.Services.Orders;
using Nop.Web.Controllers;

namespace Apt.Nop.Plugin.Misc.OneTimePasscode.Controllers;
[AutoValidateAntiforgeryToken]
public class OneTimePasscodeController : BasePublicController
{
    #region Fields

    private readonly OtpSettings _otpSettings;
    private readonly IAuthenticationService _authenticationService;
    private readonly IEventPublisher _eventPublisher;
    private readonly IStoreContext _storeContext;
    private readonly INotificationService _notificationService;
    private readonly ILocalizationService _localizationService;
    private readonly ITwilioService _twilioService;
    private readonly ICustomGenericAttributeService _genericAttributeService;
    private readonly IWorkContext _workContext;
    private readonly ICustomerService _customerService;
    private readonly ICustomerActivityService _customerActivityService;
    private readonly ICustomWorkflowMessageService _workflowMessageService;
    private readonly IShoppingCartService _shoppingCartService;
    private readonly ILogger _logger;
    #endregion

    #region Ctor

    public OneTimePasscodeController(
        IStoreContext storeContext,
        INotificationService notificationService,
        ILocalizationService localizationService, ITwilioService twilioService, ICustomGenericAttributeService genericAttributeService, IWorkContext workContext, ICustomerService customerService, ICustomerActivityService customerActivityService, ICustomWorkflowMessageService workflowMessageService, ILogger logger, IShoppingCartService shoppingCartService, IAuthenticationService authenticationService, IEventPublisher eventPublisher, OtpSettings otpSettings)
    {
        _storeContext = storeContext;
        _notificationService = notificationService;
        _localizationService = localizationService;
        _twilioService = twilioService;
        _genericAttributeService = genericAttributeService;
        _workContext = workContext;
        _customerService = customerService;
        _customerActivityService = customerActivityService;
        _workflowMessageService = workflowMessageService;
        _logger = logger;
        _shoppingCartService = shoppingCartService;
        _authenticationService = authenticationService;
        _eventPublisher = eventPublisher;
        _otpSettings = otpSettings;
    }

    #endregion

    #region Methods
    [HttpPost("apt/request-otp")]
    public virtual async Task<IActionResult> RequestLoginOtp(string email, bool generateOtp = true)
    {
        if (!CommonHelper.IsValidEmail(email))
        {
            return Json(new { errorMessage = "Please enter a valid email address." });
        }

        var markup = await this.RenderPartialViewToStringAsync(
           $"{OtpConstants.PathToPlugin}/views/_Otp.Validate.cshtml",
            new OtpLoginModel
            {
                Email = MaskEmail(email)
            });

        var customer = await _customerService.GetCustomerByEmailAsync(email.Trim());
        if (customer == null || !customer.Active || customer.Deleted)
        {
            return this.OtpJsonSuccess(new
            {
                markup
            });
        }

        if (!generateOtp)
        {
            return this.OtpJsonSuccess(new { markup });
        }

        var (existingOtp, otpCreatedUpdatedOn) =
            await _genericAttributeService.GetAttributeWithCreateUpdateDateAsync<string>(customer,
                OtpConstants.OtpLoginCode_GA_KEY);

        if (!string.IsNullOrEmpty(existingOtp)
            && otpCreatedUpdatedOn.Value.AddSeconds(_otpSettings.OtpGenerationIntervalSeconds) > DateTime.UtcNow)
        {
            var availableAt = otpCreatedUpdatedOn.Value.AddSeconds(_otpSettings.OtpGenerationIntervalSeconds);
            var secondsLeft = (int)Math.Ceiling((availableAt - DateTime.UtcNow).TotalSeconds);

            return this.OtpJsonError(
                $"Please wait at least {_otpSettings.OtpGenerationIntervalSeconds} seconds before requesting another passcode.",
                new { prematureOtpRequest = true, canResendInSeconds = Math.Max(0, secondsLeft) });
        }

        var loginOtpCode = GeneratePasswordRecoverToken(6);

        var otpBytes = Encoding.UTF8.GetBytes(loginOtpCode);
        var saltBytes = customer.CustomerGuid.ToByteArray();

        var saltedBytes = CombineBytes(otpBytes, saltBytes);
        var hash = HashHelper.CreateHash(saltedBytes, "SHA256");

        await _genericAttributeService.SaveAttributeAsync(customer,
            OtpConstants.OtpLoginCode_GA_KEY, hash);

        var store = await _storeContext.GetCurrentStoreAsync();
        var language = await _workContext.GetWorkingLanguageAsync();

        var emailIds =
            await _workflowMessageService.SendOtpPasscodeNotificationAsync(customer, loginOtpCode, store.Id,
                language.Id);
        if (!emailIds.Any())
        {
            await _logger.ErrorAsync("An error occurred attempting to send an OTP email.", null,
                await _workContext.GetCurrentCustomerAsync());
            return this.OtpJsonError("An error occurred and no email was sent");
        }

        await _customerActivityService.InsertActivityAsync(customer, OtpConstants.OtpLoginActivitySystemName,
            await _localizationService.GetResourceAsync("ActivityLog.PublicStore.OtpRequest"));

        return this.OtpJsonSuccess(new
        {
            markup,
            canResendInSeconds = _otpSettings.OtpGenerationIntervalSeconds
        });

    }

    [HttpPost("apt/otp-login")]
    public virtual async Task<IActionResult> OtpLogin(OtpLoginModel model)
    {
        var customer = await _customerService.GetCustomerByEmailAsync(model.Email.Trim());
        if (customer == null || !customer.Active || customer.Deleted)
        {
            return this.OtpJsonError("Incorrect code");
        }

        var (storedOtp, otpCreatedUpdatedOn) = await _genericAttributeService.GetAttributeWithCreateUpdateDateAsync<string>(customer,
            OtpConstants.OtpLoginCode_GA_KEY);

        var recentlyAttemptedDate = await _genericAttributeService.GetAttributeAsync<DateTime?>(customer,
            OtpConstants.OtpLoginLastAttemptedOn_GA_KEY);

        if (recentlyAttemptedDate.HasValue && recentlyAttemptedDate.Value.AddSeconds(_otpSettings.OtpValidationIntervalSeconds) > DateTime.UtcNow)
        {
            return this.OtpJsonError($"Please wait at least {_otpSettings.OtpValidationIntervalSeconds} seconds in between attempts");
        }

        if (otpCreatedUpdatedOn.Value.AddMinutes(_otpSettings.OtpExpiresAfterMinutes) < DateTime.UtcNow)
        {
            return this.OtpJsonError("This code has expired. Please resend a new one to continue.");
        }

        await _genericAttributeService.SaveAttributeAsync<DateTime?>(customer,
            OtpConstants.OtpLoginLastAttemptedOn_GA_KEY, DateTime.UtcNow);

        var otpBytes = Encoding.UTF8.GetBytes(model.Otp);
        var saltBytes = customer.CustomerGuid.ToByteArray();

        var saltedBytes = CombineBytes(otpBytes, saltBytes);

        var loginOtpCodeHash = HashHelper.CreateHash(saltedBytes, "SHA256");

        if (storedOtp == null || storedOtp != loginOtpCodeHash?.Trim())
        {
            return this.OtpJsonError("Incorrect code");
        }

        await _shoppingCartService.MigrateShoppingCartAsync(await _workContext.GetCurrentCustomerAsync(), customer, true);
        await _authenticationService.SignInAsync(customer, false);
        await _workContext.SetCurrentCustomerAsync(customer);
        await _eventPublisher.PublishAsync(new CustomerLoggedinEvent(customer));
        await _genericAttributeService.SaveAttributeAsync(customer, OtpConstants.OtpLoginCode_GA_KEY, (string)null);
        await _customerActivityService.InsertActivityAsync(customer, OtpConstants.OtpLoginActivitySystemName,
            await _localizationService.GetResourceAsync("ActivityLog.PublicStore.OtpLogin"));

        await _customerService.UpdateCustomerAsync(customer);

        _notificationService.SuccessNotification($"Logged-in as {model.Email}.");

        return this.OtpJsonSuccess();
    }

    private string GeneratePasswordRecoverToken(int length)
    {
        var random = new Random();
        const string chars = "0123456789";
        return new string(Enumerable.Range(1, length).Select(_ => chars[random.Next(chars.Length)]).ToArray());
    }

    private static byte[] CombineBytes(byte[] first, byte[] second)
    {
        var result = new byte[first.Length + second.Length];
        Buffer.BlockCopy(first, 0, result, 0, first.Length);
        Buffer.BlockCopy(second, 0, result, first.Length, second.Length);
        return result;
    }

    public static string MaskEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return string.Empty;

        var parts = email.Split('@');
        if (parts.Length != 2)
            return email;

        var username = parts[0];
        var domain = parts[1];

        if (username.Length <= 1)
            return $"*@{domain}";

        var visible = username[0];
        var maskedLength = username.Length - 1;
        var mask = new string('*', maskedLength);

        return $"{visible}{mask}@{domain}";
    }
    #endregion
}
