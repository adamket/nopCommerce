using Nop.Core.Domain.Customers;
using Nop.Core.Infrastructure;
using Nop.Services.Customers;

namespace Apt.Nop.Plugin.Misc.Booster.Extensions;
public static class CustomerExtensions
{
    public static async Task<string> GetCustomerRoleIdsStrDescAsync(this Customer customer, ICustomerService customerService = null, BoosterSettings settings = null)
    {
        customerService ??= EngineContext.Current.Resolve<ICustomerService>();
        settings ??= EngineContext.Current.Resolve<BoosterSettings>();

        if (settings.PageModifyingCustomerRoleIds == null || !settings.PageModifyingCustomerRoleIds.Any())
            return string.Empty;

        var customerRoles = await customerService.GetCustomerRolesAsync(customer, true);
        var relevantRoles = customerRoles.Where(cr => settings.PageModifyingCustomerRoleIds.Contains(cr.Id));

        var roleIds = relevantRoles.Select(cr => cr.Id).OrderByDescending(crId => crId).ToList();
        var rolesStr = string.Join(",", roleIds);
        return rolesStr;
    }
}
