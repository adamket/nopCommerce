using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nop.Core.Configuration;

namespace Apt.Nop.Plugin.Misc.OneTimePasscode;
public class OneTimePasscodeSettings : ISettings
{
    public string TwilioAccountSid {get;set;}
    public string TwilioAuthToken { get; set; }
    public string TwilioFromNumber { get; set; }

}
