using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;

/// <summary>
/// Builds the variant-axis value for one resolved Akeneo axis attribute. Shared
/// by the product sync and catalog binding so both derive the same option
/// identity (and therefore the same variant-axis mapping key).
/// </summary>
public static class AkeneoVariantAxisValueFactory
{
    public static AkeneoVariantAxisValue Create(
        string attributeCode,
        AkeneoResolvedProductValue resolved)
    {
        if (resolved == null)
            return null;

        string optionCode = null;

        if (resolved.RawData.HasValue)
        {
            var data = resolved.RawData.Value;
            optionCode = data.ValueKind switch
            {
                JsonValueKind.String => data.GetString(),
                JsonValueKind.Number => data.GetRawText(),
                JsonValueKind.Array => data.EnumerateArray()
                    .Select(item => item.ValueKind == JsonValueKind.String
                        ? item.GetString()
                        : item.GetRawText())
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
                _ => null
            };
        }

        var displayName = resolved.DisplayValues?.FirstOrDefault(value =>
                              !string.IsNullOrWhiteSpace(value))
                          ?? resolved.DisplayValue
                          ?? optionCode;

        if (string.IsNullOrWhiteSpace(displayName) &&
            string.IsNullOrWhiteSpace(optionCode))
        {
            return null;
        }

        return new AkeneoVariantAxisValue
        {
            AkeneoAttributeCode = attributeCode,
            AkeneoOptionCode = optionCode?.Trim(),
            DisplayName = displayName?.Trim()
        };
    }

    /// <summary>
    /// The entity-mapping code that binds an axis option on one nopCommerce
    /// parent product to its <c>ProductAttributeValue</c>.
    /// </summary>
    public static string BuildMappingCode(
        int parentProductId,
        AkeneoVariantAxisValue axisValue) =>
        $"variant-axis:{parentProductId}:{axisValue.AkeneoAttributeCode}:" +
        $"{axisValue.AkeneoOptionCode ?? axisValue.DisplayName}";
}
