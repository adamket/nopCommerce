using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nop.Core;
using Nop.Services.Common;

namespace Apt.Nop.Plugin.Misc.OneTimePasscode.Services;
public interface ICustomGenericAttributeService : IGenericAttributeService
{
    public Task<(TPropType, DateTime?)> GetAttributeWithCreateUpdateDateAsync<TPropType>(BaseEntity entity, string key, int storeId = 0, TPropType defaultValue = default);
}
