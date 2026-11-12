using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Extensions;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;

public sealed class AkeneoAssetMappingModelFactory(
    IAkeneoApiClient apiClient,
    IAkeneoAssetMappingService assetMappingService,
    IAkeneoFamilyMappingService familyMappingService = null)
    : IAkeneoAssetMappingModelFactory
{
    public async Task<AkeneoAssetMappingListModel> PrepareListModelAsync(
        string familyCode = null)
    {
        familyCode = familyCode.TrimOrNull();
        var model = new AkeneoAssetMappingListModel
        {
            AkeneoFamilyCode = familyCode
        };

        try
        {
            var families = await apiClient.GetFamiliesAsync();
            model.AvailableAkeneoFamilies = families
                .Where(family => !string.IsNullOrWhiteSpace(family.Code))
                .OrderBy(family => family.GetLabel("en_US"), StringComparer.OrdinalIgnoreCase)
                .Select(family => new SelectListItem
                {
                    Value = family.Code.Trim(),
                    Text = family.GetLabelWithCode("en_US")
                })
                .ToList();
        }
        catch (Exception ex)
        {
            model.Warnings.Add($"Akeneo families could not be loaded: {ex.Message}");
        }

        try
        {
            var attributes = await apiClient.GetAttributesAsync();
            model.AvailableSources = attributes
                .Where(attribute => IsAssetSource(attribute.Type))
                .OrderBy(attribute => attribute.GetDisplayName("en_US"), StringComparer.OrdinalIgnoreCase)
                .Select(attribute => new AkeneoAssetSourceOptionModel
                {
                    Code = attribute.Code,
                    Label = attribute.GetDisplayName("en_US"),
                    AttributeType = attribute.Type,
                    SourceTypeId = attribute.Type == "pim_catalog_asset_collection"
                        ? (int)AkeneoAssetSourceType.AssetCollection
                        : (int)AkeneoAssetSourceType.ProductMediaAttribute,
                    AssetFamilyCode = attribute.ReferenceDataName,
                    Localizable = attribute.Localizable,
                    Scopable = attribute.Scopable
                })
                .ToList();
        }
        catch (Exception ex)
        {
            model.Warnings.Add($"Akeneo media attributes could not be loaded: {ex.Message}");
        }

        var mappings = familyCode == null
            ? (await assetMappingService.GetAllAsync())
                .Where(mapping => string.IsNullOrWhiteSpace(mapping.AkeneoFamilyCode))
                .ToList()
            : await assetMappingService.GetEffectiveMappingsAsync(familyCode);

        model.Mappings = mappings
            .Select(mapping => PrepareMappingModel(mapping, familyCode))
            .ToList();

        if (familyCode != null && familyMappingService != null)
        {
            try
            {
                var variants = await apiClient.GetFamilyVariantsAsync(familyCode);
                var combinationVariants = new List<AkeneoFamilyVariantDefinition>();
                foreach (var variant in variants.Where(item => !string.IsNullOrWhiteSpace(item.Code)))
                {
                    var familyMapping = await familyMappingService.GetEffectiveMappingAsync(familyCode, variant.Code.Trim());
                    if (familyMapping is { Enabled: true } &&
                        familyMapping.VariantRelationshipMode == AkeneoVariantRelationshipMode.ProductAttributeCombinations)
                    {
                        combinationVariants.Add(variant);
                    }
                }

                foreach (var warning in BuildCombinationPictureWarnings(mappings, combinationVariants))
                    model.Warnings.Add(warning);
            }
            catch (Exception ex)
            {
                model.Warnings.Add($"Akeneo family variants could not be loaded to check combination pictures: {ex.Message}");
            }
        }

        return model;
    }

    /// <summary>
    /// A Variant products picture mapping whose attributes are all set above
    /// the variant level gives every combination of a product the same picture.
    /// </summary>
    internal static IEnumerable<string> BuildCombinationPictureWarnings(
        IEnumerable<AkeneoAssetMapping> mappings,
        IEnumerable<AkeneoFamilyVariantDefinition> combinationFamilyVariants)
    {
        var variants = combinationFamilyVariants.ToList();
        if (variants.Count == 0)
            yield break;

        foreach (var mapping in mappings.Where(mapping =>
                     mapping.Enabled &&
                     (AkeneoAssetDestinationType)mapping.DestinationTypeId == AkeneoAssetDestinationType.ProductPicture &&
                     AkeneoAttributeMappingScopeHelper.NormalizeConfiguredScope(mapping.EntityScopeId)
                         .HasFlag(AkeneoAttributeMappingEntityScope.VariantProduct)))
        {
            var sources = new[] { mapping.SourceAttributeCode, mapping.FallbackSourceAttributeCode }
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code.Trim())
                .ToList();
            if (sources.Count == 0)
                continue;

            var affected = variants
                .Where(variant => !sources.Any(source => variant.VariantAttributeSets.Any(set =>
                    set.Attributes.Any(attribute => string.Equals(attribute?.Trim(), source, StringComparison.OrdinalIgnoreCase)))))
                .Select(variant => variant.Code.Trim())
                .ToList();
            if (affected.Count == 0)
                continue;

            yield return
                $"Asset mapping '{mapping.Name ?? mapping.MappingKey}' gives attribute combinations their picture, but " +
                $"'{string.Join("' / '", sources)}' is set on the product model in family variant {string.Join(", ", affected.Select(code => $"'{code}'"))}, " +
                "so every combination of a product gets the same picture. Use an attribute set at the variant or sub-model level.";
        }
    }

    public AkeneoAssetMappingModel PrepareMappingModel(
        AkeneoAssetMapping mapping,
        string selectedFamilyCode = null)
    {
        return new AkeneoAssetMappingModel
        {
            Id = mapping.Id,
            MappingKey = mapping.MappingKey,
            Name = mapping.Name,
            Enabled = mapping.Enabled,
            AkeneoFamilyCode = selectedFamilyCode,
            IsInherited = !string.IsNullOrWhiteSpace(selectedFamilyCode) &&
                string.IsNullOrWhiteSpace(mapping.AkeneoFamilyCode),
            SourceTypeId = mapping.SourceTypeId,
            SourceAttributeCode = mapping.SourceAttributeCode,
            FallbackSourceAttributeCode = mapping.FallbackSourceAttributeCode,
            AssetFamilyCode = mapping.AssetFamilyCode,
            AssetMediaAttributeCode = mapping.AssetMediaAttributeCode,
            AssetMediaType = mapping.AssetMediaType,
            DestinationTypeId = mapping.DestinationTypeId,
            StorageModeId = mapping.StorageModeId,
            EntityScopeId = mapping.EntityScopeId,
            RoleAttributeCode = mapping.RoleAttributeCode,
            RoleValuesCsv = mapping.RoleValuesCsv,
            SortOrderAttributeCode = mapping.SortOrderAttributeCode,
            AltTextTemplate = mapping.AltTextTemplate,
            TitleTextTemplate = mapping.TitleTextTemplate,
            SeoFilenameTemplate = mapping.SeoFilenameTemplate,
            CustomPropertyKey = mapping.CustomPropertyKey,
            DisplayOrder = mapping.DisplayOrder,
            MaxAssets = mapping.MaxAssets
        };
    }

    private static bool IsAssetSource(string type) =>
        type is "pim_catalog_image" or
            "pim_catalog_file" or
            "pim_catalog_asset_collection";

   
}
