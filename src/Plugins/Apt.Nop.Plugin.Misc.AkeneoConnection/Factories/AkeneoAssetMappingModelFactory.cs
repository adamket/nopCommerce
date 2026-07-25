using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Extensions;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;

public sealed class AkeneoAssetMappingModelFactory(
    IAkeneoApiClient apiClient,
    IAkeneoAssetMappingService assetMappingService)
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
                .OrderBy(family => family.GetDisplayName("en_US"), StringComparer.OrdinalIgnoreCase)
                .Select(family => new SelectListItem
                {
                    Value = family.Code,
                    Text = family.GetDisplayName("en_US")
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

        return model;
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
