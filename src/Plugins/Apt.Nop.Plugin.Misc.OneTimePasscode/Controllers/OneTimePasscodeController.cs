using System.Text;
using Apt.Nop.Plugin.Misc.OneTimePasscode.Extensions;
using Apt.Nop.Plugin.Misc.OneTimePasscode.Models;
using Apt.Nop.Plugin.Misc.OneTimePasscode.Services;
using DocumentFormat.OpenXml.EMMA;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Logging;
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
    private readonly IQueuedEmailService _queuedEmailService;
    private const string _hashSeparator = "_";
    #endregion

    #region Ctor

    public OneTimePasscodeController(
        IStoreContext storeContext,
        INotificationService notificationService,
        ILocalizationService localizationService, ITwilioService twilioService, ICustomGenericAttributeService genericAttributeService, IWorkContext workContext, ICustomerService customerService, ICustomerActivityService customerActivityService, ICustomWorkflowMessageService workflowMessageService, ILogger logger, IShoppingCartService shoppingCartService, IAuthenticationService authenticationService, IEventPublisher eventPublisher, OtpSettings otpSettings, IQueuedEmailService queuedEmailService)
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
        _queuedEmailService = queuedEmailService;
    }

    #endregion

    #region Methods
    [HttpPost("apt/request-otp")]
    public virtual async Task<IActionResult> RequestLoginOtp(string email, bool bypassSend = false)
    {
        if (!CommonHelper.IsValidEmail(email))
        {
            return Json(new { errorMessage = await _localizationService.GetResourceAsync("apt.plugins.misc.otp.errors.invalid-email") });
        }

        var codeDigitCount = _otpSettings.CodeDigitCount is >= 3 and <= 6 ? _otpSettings.CodeDigitCount : 6;
        var markup = await this.RenderPartialViewToStringAsync(
           $"{OtpConstants.PathToPlugin}/views/_Otp.Validate.cshtml",
            new OtpLoginModel
            {
                Email = MaskEmail(email),
                CodeExpiryMinutes = _otpSettings.OtpExpiresAfterMinutes,
                CodeDigitCount = codeDigitCount
            });

        //forward straight to code input without generating or emailing code (when user already has valid code but page refreshed)
        if (bypassSend)
        {
            return this.OtpJsonSuccess(new { markup });
        }

        var utcNow = DateTime.UtcNow;
        var currentCustomer = await _workContext.GetCurrentCustomerAsync();
        var targetCustomer = await _customerService.GetCustomerByEmailAsync(email.Trim());

        if ((!targetCustomer?.Active ?? false) || targetCustomer?.Deleted == true)
        {
            return this.OtpJsonError(await _localizationService.GetResourceAsync("apt.plugins.misc.otp.errors.customer-inactive"));
        }

        var lastResendCustomer = targetCustomer ?? currentCustomer;
        var lastResendOn = await _genericAttributeService.GetAttributeAsync<DateTime?>(lastResendCustomer, OtpConstants.GenericAttributeKeys.OtpLastResentOn);
        if (lastResendOn.HasValue)
        {
            if (lastResendOn.Value.AddSeconds(_otpSettings.OtpRequestIntervalSeconds) > utcNow)
            {
                return await GetEarlyOtpRequestErrorResult(lastResendOn.Value);
            }
        }

        if (targetCustomer == null)
        {
            if (_otpSettings.PreventUserEnumeration)
            {
                await _genericAttributeService.SaveAttributeAsync<DateTime?>(lastResendCustomer, OtpConstants.GenericAttributeKeys.OtpLastResentOn, utcNow);
                return this.OtpJsonSuccess(new
                {
                    markup,
                    canResendInSeconds = _otpSettings.OtpRequestIntervalSeconds
                });
            }

            return this.OtpJsonError(await _localizationService.GetResourceAsync("apt.plugins.misc.otp.errors.customer-not-found"));
        }

        var (existingOtp, otpCreatedUpdatedOn) =
            await _genericAttributeService.GetAttributeWithCreateUpdateDateAsync<string>(targetCustomer,
                OtpConstants.GenericAttributeKeys.LoginCode);

        //TODO UPDATE MSG TEMPLATE
        //double validate premature request
        if (!string.IsNullOrEmpty(existingOtp)
            && otpCreatedUpdatedOn.Value.AddSeconds(_otpSettings.OtpRequestIntervalSeconds) > utcNow)
        {
            return await GetEarlyOtpRequestErrorResult(otpCreatedUpdatedOn.Value);
        }

        await _genericAttributeService.SaveAttributeAsync<DateTime?>(targetCustomer, OtpConstants.GenericAttributeKeys.OtpLastResentOn, utcNow);

        var isCurrentTokenExpired = otpCreatedUpdatedOn.HasValue && (otpCreatedUpdatedOn.Value.AddMinutes(_otpSettings.OtpExpiresAfterMinutes) < utcNow);
        var digitCount = int.TryParse(existingOtp?.Split(_hashSeparator).LastOrDefault(), out var result) ? result : codeDigitCount;

        if (!isCurrentTokenExpired && digitCount == codeDigitCount) 
        {
            var emailIdsToRequeue =
                await _genericAttributeService.GetAttributeAsync<List<int>>(targetCustomer, OtpConstants.GenericAttributeKeys
                    .QueuedEmailIds);

            if (emailIdsToRequeue != null && emailIdsToRequeue.Any())
            {
                var queuedEmails = await _queuedEmailService.GetQueuedEmailsByIdsAsync(emailIdsToRequeue.ToArray());
                await _queuedEmailService.RequeueQueuedEmailsAsync(queuedEmails);

                return this.OtpJsonSuccess(new
                {
                    markup,
                    canResendInSeconds = _otpSettings.OtpRequestIntervalSeconds,
                    CodeDigitCount = digitCount
                });
            }
        }

        var loginOtpCode = GeneratePasswordRecoverToken(codeDigitCount);

        var otpBytes = Encoding.UTF8.GetBytes(loginOtpCode);
        var saltBytes = targetCustomer.CustomerGuid.ToByteArray();

        var saltedBytes = CombineBytes(otpBytes, saltBytes);
        var hash = HashHelper.CreateHash(saltedBytes, "SHA256");

        await _genericAttributeService.SaveAttributeAsync(targetCustomer,
            OtpConstants.GenericAttributeKeys.LoginCode, $"{hash}{_hashSeparator}{_otpSettings.CodeDigitCount}");

        var store = await _storeContext.GetCurrentStoreAsync();
        var language = await _workContext.GetWorkingLanguageAsync();

        var queuedEmailIds =
            await _workflowMessageService.SendOtpPasscodeNotificationAsync(targetCustomer, loginOtpCode, store.Id,
                language.Id);
        if (!queuedEmailIds.Any())
        {
            await _logger.InsertLogAsync(LogLevel.Error, "A one time passcode was requested, but email was unable to be sent.", "Please verify the logs directly prior, and that your message templates are properly configured.",
                await _workContext.GetCurrentCustomerAsync());
            return this.OtpJsonError(await _localizationService.GetResourceAsync("apt.plugins.misc.otp.errors.no-email-sent"));
        }

        await _genericAttributeService.SaveAttributeAsync(targetCustomer,
            OtpConstants.GenericAttributeKeys.QueuedEmailIds, queuedEmailIds);

        await _customerActivityService.InsertActivityAsync(targetCustomer, OtpConstants.OtpLoginActivitySystemName,
            await _localizationService.GetResourceAsync("apt.plugins.misc.otp.activity-log.public-store.otp-requested"));

        return this.OtpJsonSuccess(new
        {
            markup,
            canResendInSeconds = _otpSettings.OtpRequestIntervalSeconds
        });

    }

    [HttpPost("apt/otp-login")]
    public virtual async Task<IActionResult> OtpLogin(OtpLoginModel model)
    {
        var currentCustomer = await _workContext.GetCurrentCustomerAsync();
        var targetCustomer = await _customerService.GetCustomerByEmailAsync(model.Email.Trim());

        if ((!targetCustomer?.Active ?? false) || targetCustomer?.Deleted == true)
        {
            return this.OtpJsonError(await _localizationService.GetResourceAsync("apt.plugins.misc.otp.errors.customer-inactive"));
        }

        var utcNow = DateTime.UtcNow;

        var lastAttemptCustomer = targetCustomer ?? currentCustomer;
        var lastAttemptedOtpValidation = await _genericAttributeService.GetAttributeAsync<DateTime?>(lastAttemptCustomer, OtpConstants.GenericAttributeKeys.LoginLastAttempted);
        if (lastAttemptedOtpValidation.HasValue &&
            lastAttemptedOtpValidation.Value.AddSeconds(_otpSettings.OtpValidationIntervalSeconds) >
            utcNow)
        {
            return await GetEarlyOtpValidationErrorResult();
        }

        await _genericAttributeService.SaveAttributeAsync<DateTime?>(lastAttemptCustomer,
            OtpConstants.GenericAttributeKeys.LoginLastAttempted, utcNow);

        if (targetCustomer == null)
        {
            return this.OtpJsonError(await _localizationService.GetResourceAsync("apt.plugins.misc.otp.errors.incorrect-code"));
        }

        var (storedOtp, otpCreatedUpdatedOn) = await _genericAttributeService.GetAttributeWithCreateUpdateDateAsync<string>(targetCustomer,
            OtpConstants.GenericAttributeKeys.LoginCode);

        if (otpCreatedUpdatedOn.Value.AddMinutes(_otpSettings.OtpExpiresAfterMinutes) < utcNow)
        {
            return this.OtpJsonError(await _localizationService.GetResourceAsync("apt.plugins.misc.otp.errors.otp-expired"));
        }

        await _genericAttributeService.SaveAttributeAsync<DateTime?>(targetCustomer,
            OtpConstants.GenericAttributeKeys.LoginLastAttempted, utcNow);


        storedOtp = storedOtp.Split(_hashSeparator).FirstOrDefault();

        var otpBytes = Encoding.UTF8.GetBytes(model.Otp);
        var saltBytes = targetCustomer.CustomerGuid.ToByteArray();
        var saltedBytes = CombineBytes(otpBytes, saltBytes);
        var loginOtpCodeHash = HashHelper.CreateHash(saltedBytes, "SHA256");

        if (storedOtp == null || storedOtp != loginOtpCodeHash?.Trim())
        {
            return this.OtpJsonError(await _localizationService.GetResourceAsync("apt.plugins.misc.otp.errors.incorrect-code"));
        }

        await _shoppingCartService.MigrateShoppingCartAsync(await _workContext.GetCurrentCustomerAsync(), targetCustomer, true);
        await _authenticationService.SignInAsync(targetCustomer, false);
        await _workContext.SetCurrentCustomerAsync(targetCustomer);
        await _eventPublisher.PublishAsync(new CustomerLoggedinEvent(targetCustomer));

        //delete attributes
        await _genericAttributeService.SaveAttributeAsync(targetCustomer, OtpConstants.GenericAttributeKeys.LoginCode, (string)null);
        await _genericAttributeService.SaveAttributeAsync(targetCustomer, OtpConstants.GenericAttributeKeys.QueuedEmailIds, (string)null);
        await _customerActivityService.InsertActivityAsync(targetCustomer, OtpConstants.OtpLoginActivitySystemName,
            await _localizationService.GetResourceAsync("apt.plugins.misc.otp.activity-log.public-store.otp-login"));

        await _customerService.UpdateCustomerAsync(targetCustomer);

        _notificationService.SuccessNotification(StringExtensions.SafeFormat(
            await _localizationService.GetResourceAsync("apt.plugins.misc.otp.notification.logged-in"), model.Email));

        var redirectUrl = !string.IsNullOrEmpty(model.RedirectUrlPath) ? $"/{model.RedirectUrlPath.TrimStart('/')}" : "/";

        return this.OtpJsonSuccess(new
        {
            redirectUrl
        });
    }


    private async Task<IActionResult> GetEarlyOtpValidationErrorResult()
    {
        return this.OtpJsonError(StringExtensions.SafeFormat(
            await _localizationService.GetResourceAsync(
                "apt.plugins.misc.otp.errors.requested-validation-too-soon"),
            _otpSettings.OtpValidationIntervalSeconds));
    }

    private async Task<IActionResult> GetEarlyOtpRequestErrorResult(DateTime otpCreatedUpdatedOn)
    {
        var availableAt = otpCreatedUpdatedOn.AddSeconds(_otpSettings.OtpRequestIntervalSeconds);
        var secondsLeft = (int)Math.Ceiling((availableAt - DateTime.UtcNow).TotalSeconds);

        var message = StringExtensions.SafeFormat(
            await _localizationService.GetResourceAsync("apt.plugins.misc.otp.errors.requested-generation-too-soon"), _otpSettings.OtpRequestIntervalSeconds);

        return this.OtpJsonError(message,
            new { prematureOtpRequest = true, canResendInSeconds = Math.Max(0, secondsLeft) });
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
