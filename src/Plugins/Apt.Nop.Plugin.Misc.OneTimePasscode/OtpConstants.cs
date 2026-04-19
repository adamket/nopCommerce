using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Apt.Nop.Plugin.Misc.OneTimePasscode;
public static class OtpConstants
{
    public const string OneTimePasscodePluginSystemName = "Apt.Misc.OneTimePasscode";
    public const string OtpLoginCode_GA_KEY = "otp-code";
    public const string OtpCurrentCustomerNextRegenAttemptAllowedOn_GA_KEY = "otp.current-customer.last-code-generation-attempt";
    public const string OtpLoginLastAttempted_GA_KEY = "otp.login-last-attempted";

    public const string MessageTemplateSystemName = "OtpRequested.CustomerNotificaton";
    public const string OtpLoginActivitySystemName = "Apt.OneTimePasscode.Customer.LoginWithOtp";
    public const string PathToPlugin = "~/Plugins/Apt.Misc.OneTimePasscode";
}
