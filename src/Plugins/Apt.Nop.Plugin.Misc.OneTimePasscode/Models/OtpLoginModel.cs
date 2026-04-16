using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Apt.Nop.Plugin.Misc.OneTimePasscode.Models;
public class OtpLoginModel
{
    public string Email { get; set; }
    public string Otp { get; set; }
}
