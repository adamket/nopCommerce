using Nop.Core.Domain.Customers;
using Nop.Services.Customers;

namespace Apt.Nop.Plugin.Misc.NopCms.Services;
public class NopCmsHelper(ICustomerService customerService)
{
    public async Task AddCustomerRoleIfNotExistAsync(string roleSystemName, string roleName)
    {
        var existingCustomerRole = await customerService.GetCustomerRoleBySystemNameAsync(roleSystemName);
        if (existingCustomerRole != null)
        {
            return;
        }

        var role = new CustomerRole
        {
            Active = true,
            Name = roleName,
            SystemName = roleSystemName,
        };

        await customerService.InsertCustomerRoleAsync(role);

    }

}
