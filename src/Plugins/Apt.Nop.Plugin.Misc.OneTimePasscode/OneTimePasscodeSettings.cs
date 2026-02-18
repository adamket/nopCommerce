using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nop.Core.Configuration;

namespace Apt.Nop.Plugin.Misc.OneTimePasscode;
public class OneTimePasscodeSettings : ISettings
{
    public string TwilioAccountSid {get;set;}// "AC0d9b37531d65ac052764efb01ce58c04";
    public string TwilioAuthToken { get; set; } //= "ead25b2420a14a652958b12b269e5f12";
    public string TwilioFromNumber { get; set; }//= "18886218314";

}
