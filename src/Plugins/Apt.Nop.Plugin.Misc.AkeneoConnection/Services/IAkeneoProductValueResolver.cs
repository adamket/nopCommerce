using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
public interface IAkeneoProductValueResolver
{
    string GetValue(
        JsonElement product,
        string attributeCode,
        string locale = null,
        string channel = null,
        string currency = null);

    bool TryGetValue(
        JsonElement product,
        string attributeCode,
        out AkeneoResolvedProductValue resolvedValue,
        string locale = null,
        string channel = null,
        string currency = null);
}