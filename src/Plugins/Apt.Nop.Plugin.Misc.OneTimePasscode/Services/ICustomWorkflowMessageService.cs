using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nop.Core.Domain.Customers;
using Nop.Services.Messages;

namespace Apt.Nop.Plugin.Misc.OneTimePasscode.Services;
public interface ICustomWorkflowMessageService : IWorkflowMessageService
{
    Task<IList<int>> SendOtpPasscodeNotificationAsync(Customer customer, string rawOtp, int languageId, int storeId);
}
