using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;

/// <summary>
/// Resolves the effective family-variant code for a leaf product from its
/// immediate parent product model. Akeneo product payloads expose parent but
/// do not expose family_variant, so leaf synchronization must not rely on
/// <see cref="AkeneoProductDefinition.FamilyVariant"/>.
/// </summary>
public static class AkeneoLeafFamilyVariantResolver
{
    public static AkeneoLeafFamilyVariantResolution Resolve(
        AkeneoProductDefinition source,
        IReadOnlyList<AkeneoProductDefinition> ancestorsNearestFirst)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (string.IsNullOrWhiteSpace(source.Parent))
        {
            return new AkeneoLeafFamilyVariantResolution
            {
                Success = true
            };
        }

        var immediateParent = ancestorsNearestFirst?.FirstOrDefault();
        var parentCode = immediateParent?.Code?.Trim()
            ?? source.Parent?.Trim()
            ?? "(unknown product model)";
        var familyVariantCode = immediateParent?.FamilyVariant?.Trim();

        if (!string.IsNullOrWhiteSpace(familyVariantCode))
        {
            return new AkeneoLeafFamilyVariantResolution
            {
                Success = true,
                FamilyVariantCode = familyVariantCode,
                ImmediateParentCode = parentCode
            };
        }

        var productIdentity = source.Identifier?.Trim()
            ?? source.Uuid?.Trim()
            ?? "(unknown product)";

        return new AkeneoLeafFamilyVariantResolution
        {
            Success = false,
            ImmediateParentCode = parentCode,
            Error =
                $"Akeneo variant product '{productIdentity}' references parent product model '{parentCode}', but the parent's family-variant code could not be resolved. The operation will not silently fall back to the family-wide variant configuration."
        };
    }
}

public sealed class AkeneoLeafFamilyVariantResolution
{
    public bool Success { get; init; }

    public string FamilyVariantCode { get; init; }

    public string ImmediateParentCode { get; init; }

    public string Error { get; init; }
}
