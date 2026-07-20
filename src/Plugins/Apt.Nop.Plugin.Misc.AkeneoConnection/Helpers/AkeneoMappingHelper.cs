using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;

public static class AkeneoMappingHelper
{
    public static bool SetIfChanged<T>(
        T currentValue,
        T newValue,
        Action<T> setter)
    {
        if (typeof(T) == typeof(string))
        {
            var currentString = currentValue as string ?? string.Empty;
            var newString = newValue as string ?? string.Empty;

            if (string.Equals(currentString, newString, StringComparison.Ordinal))
                return false;

            setter((T)(object)newString);
            return true;
        }

        if (EqualityComparer<T>.Default.Equals(currentValue, newValue))
            return false;

        setter(newValue);
        return true;
    }

    /// <summary>
    /// Returns a stable source key for a mapping. Normal attributes use the
    /// Akeneo attribute code. Reference-entity mappings also include the chosen
    /// reference-entity field so each field can be synchronized independently.
    /// </summary>
    public static string GetSourceMappingCode(AkeneoAttributeMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);

        var attributeCode = mapping.AkeneoAttributeCode?.Trim() ?? string.Empty;

        if (!IsReferenceEntityMapping(mapping) ||
            string.IsNullOrWhiteSpace(mapping.AkeneoReferenceEntityAttributeCode))
        {
            return attributeCode;
        }

        return $"{attributeCode}::{mapping.AkeneoReferenceEntityAttributeCode.Trim()}";
    }

    public static string GetSourceDisplayName(AkeneoAttributeMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);

        var attributeCode = mapping.AkeneoAttributeCode?.Trim() ?? string.Empty;

        if (!IsReferenceEntityMapping(mapping) ||
            string.IsNullOrWhiteSpace(mapping.AkeneoReferenceEntityAttributeCode))
        {
            return attributeCode;
        }

        return $"{attributeCode}.{mapping.AkeneoReferenceEntityAttributeCode.Trim()}";
    }

    public static bool IsReferenceEntityMapping(AkeneoAttributeMapping mapping)
    {
        return mapping?.AkeneoAttributeTypeId ==
                   (int)AkeneoAttributeType.ReferenceEntity ||
               mapping?.AkeneoAttributeTypeId ==
                   (int)AkeneoAttributeType.ReferenceEntityCollection;
    }
}
