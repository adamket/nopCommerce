using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nop.Core;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Messages;
using Nop.Core.Events;
using Nop.Services.Affiliates;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Orders;
using Nop.Services.Stores;

namespace Apt.Nop.Plugin.Misc.OneTimePasscode.Services;
public class CustomWorkflowMessageService(
    CommonSettings commonSettings,
    EmailAccountSettings emailAccountSettings,
    IAddressService addressService,
    IAffiliateService affiliateService,
    ICustomerService customerService,
    IEmailAccountService emailAccountService,
    IEventPublisher eventPublisher,
    ILanguageService languageService,
    ILocalizationService localizationService,
    IMessageTemplateService messageTemplateService,
    IMessageTokenProvider messageTokenProvider,
    IOrderService orderService,
    IProductService productService,
    IQueuedEmailService queuedEmailService,
    IStoreContext storeContext,
    IStoreService storeService,
    ITokenizer tokenizer,
    MessagesSettings messagesSettings)
    : WorkflowMessageService(commonSettings, emailAccountSettings, addressService, affiliateService, customerService,
        emailAccountService, eventPublisher, languageService, localizationService, messageTemplateService,
        messageTokenProvider, orderService, productService, queuedEmailService, storeContext, storeService, tokenizer,
        messagesSettings), ICustomWorkflowMessageService
{
    public async Task<IList<int>> SendOtpPasscodeNotificationAsync(Customer customer, string rawOtp, int languageId, int storeId)
    {
        var store = await _storeService.GetStoreByIdAsync(storeId);
        var messageTemplates = await GetActiveMessageTemplatesAsync(OtpConstants.MessageTemplateSystemName, store.Id);
        if (!messageTemplates.Any())
            return new List<int>();
        languageId = await EnsureLanguageIsActiveAsync(languageId, store.Id);

        return await messageTemplates.SelectAwait(async messageTemplate =>
        {
            //email account
            var emailAccount = await GetEmailAccountOfMessageTemplateAsync(messageTemplate, languageId);

            var tokens = new List<Token>();
            await _messageTokenProvider.AddStoreTokensAsync(tokens, store, emailAccount, languageId);
            await _messageTokenProvider.AddCustomerTokensAsync(tokens, customer);
            tokens.Add(new Token("OtpLogin.Otp", rawOtp));

            //event notification
            await _eventPublisher.MessageTokensAddedAsync(messageTemplate, tokens);

            return await SendNotificationAsync(messageTemplate, emailAccount, languageId, tokens, customer.Email,
                customer.Email);
        }).ToListAsync();

    }
}

