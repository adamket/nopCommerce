using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nop.Core.Configuration;

namespace Apt.Nop.Plugin.Misc.OneTimePasscode;
public class OtpSettings : ISettings
{
    public string TwilioAccountSid {get;set;}
    public string TwilioAuthToken { get; set; }
    public string TwilioFromNumber { get; set; }

    public int OtpValidationIntervalSeconds { get; set; }
    public int OtpRequestIntervalSeconds { get; set; }

    public int OtpExpiresAfterMinutes { get; set; }

    public bool PreventUserEnumeration { get; set; }
    public int CodeDigitCount { get; set; }

}
