using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Apt.Nop.Plugin.Misc.OneTimePasscode;
public static class OtpConstants
{
    public const string OneTimePasscodePluginSystemName = "Apt.Misc.OneTimePasscode";
    public const string MessageTemplateSystemName = "OtpRequested.CustomerNotificaton";
    public const string OtpLoginActivitySystemName = "Apt.OneTimePasscode.Customer.LoginWithOtp";
    public const string PathToPlugin = "~/Plugins/Apt.Misc.OneTimePasscode";

    public class GenericAttributeKeys
    {
        
        public const string QueuedEmailIds = "apt.otp.customer.queued-email-id";
        public const string OtpLastResentOn = "apt.otp.customer.resend-last-attempted";
        public const string LoginLastAttempted = "apt.otp.customer.login-last-attempted";
        public const string LoginCode = "apt.otp.customer.login-code";
    }

}
