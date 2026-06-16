using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
public interface IAkeneoProductValueResolver
{
    string GetValue(
        JsonElement product,
        string attributeCode,
        string locale = null,
        string channel = null,
        string currency = null);
}