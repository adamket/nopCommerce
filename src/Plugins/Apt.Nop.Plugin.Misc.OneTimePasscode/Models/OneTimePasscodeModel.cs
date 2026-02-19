using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Apt.Nop.Plugin.Misc.OneTimePasscode.Models;
public class OneTimePasscodeModel
{
    public string TwilioAccountSid { get; set; } 
    public string TwilioAuthToken { get; set; } 
    public string TwilioFromNumber { get; set; }
}
