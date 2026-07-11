using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
public interface IAkeneoProductValueResolver
{
    string GetValue(
        AkeneoProductDefinition product,
        string attributeCode,
        string locale = null,
        string channel = null,
        string currency = null);

    bool TryGetValue(
        AkeneoProductDefinition product,
        string attributeCode,
        out AkeneoResolvedProductValue resolvedValue,
        string locale = null,
        string channel = null,
        string currency = null);
}