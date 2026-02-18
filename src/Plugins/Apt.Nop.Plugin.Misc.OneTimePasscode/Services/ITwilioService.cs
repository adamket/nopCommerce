using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Apt.Nop.Plugin.Misc.OneTimePasscode.Services;
public interface ITwilioService
{
    Task<(bool Success, string Response)> SendSmsAsync(string from, string to, string body);
}
