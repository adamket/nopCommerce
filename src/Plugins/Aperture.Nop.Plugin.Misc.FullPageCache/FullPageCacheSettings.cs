using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nop.Core.Configuration;

namespace Aperture.Nop.Plugin.Misc.FullPageCache;
public class FullPageCacheSettings : ISettings
{
    public List<int> PageAlteringCustomerRoleIds {  get; set; }

}
