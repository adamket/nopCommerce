using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Apt.Nop.Plugin.Misc.Booster.Services;
public interface IDebouncer
{
    void Debounce(object key, Func<CancellationToken, Task> action, TimeSpan delay);
}