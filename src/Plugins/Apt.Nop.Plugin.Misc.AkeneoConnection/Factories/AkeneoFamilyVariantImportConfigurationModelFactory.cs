using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;

public class AkeneoFamilyVariantImportConfigurationModelFactory
    : IAkeneoFamilyVariantImportConfigurationModelFactory
{
    private readonly IAkeneoFamilyVariantImportConfigurationService _configurationService;
    private readonly IAkeneoApiClient _akeneoApiClient;
    private readonly IProductAttributeService _productAttributeService;

    public AkeneoFamilyVariantImportConfigurationModelFactory(
        IAkeneoFamilyVariantImportConfigurationService configurationService,
        IAkeneoApiClient akeneoApiClient,
        IProductAttributeService productAttributeService)
    {
        _configurationService = configurationService;
        _akeneoApiClient = akeneoApiClient;
        _productAttributeService = productAttributeService;
    }

    public async Task<AkeneoFamilyVariantImportConfigurationListModel> PrepareListModelAsync()
    {
        var configurations = await _configurationService.GetAllAsync();

        var model = new AkeneoFamilyVariantImportConfigurationListModel();

        model.Configurations = configurations
            .Select(x => new AkeneoFamilyVariantImportConfigurationModel
            {
                Id = x.Id,
                AkeneoFamilyCode = x.AkeneoFamilyCode,
                Enabled = x.Enabled,
                VariantRelationshipModeId = x.VariantRelationshipModeId,
                PreserveExistingNopVariantStructure = x.PreserveExistingNopVariantStructure,
                AssociatedProductAttributeId = x.AssociatedProductAttributeId,
                AssociatedValueNameTemplate = x.AssociatedValueNameTemplate,
                HideChildProductsWhenRepresentedByParent = x.HideChildProductsWhenRepresentedByParent,
                DisplayOrder = x.DisplayOrder
            })
            .ToList();

        return model;
    }

    public async Task<AkeneoFamilyVariantImportConfigurationModel> PrepareModelAsync(
        AkeneoFamilyVariantImportConfigurationModel model = null,
        AkeneoFamilyVariantImportConfiguration configuration = null)
    {
        model ??= new AkeneoFamilyVariantImportConfigurationModel();

        if (configuration != null)
        {
            model.Id = configuration.Id;
            model.AkeneoFamilyCode = configuration.AkeneoFamilyCode;
            model.Enabled = configuration.Enabled;
            model.VariantRelationshipModeId = configuration.VariantRelationshipModeId;
            model.PreserveExistingNopVariantStructure = configuration.PreserveExistingNopVariantStructure;
            model.AssociatedProductAttributeId = configuration.AssociatedProductAttributeId;
            model.AssociatedValueNameTemplate = configuration.AssociatedValueNameTemplate;
            model.HideChildProductsWhenRepresentedByParent = configuration.HideChildProductsWhenRepresentedByParent;
            model.DisplayOrder = configuration.DisplayOrder;

            var axisMappings = await _configurationService.GetAxisMappingsAsync(configuration.Id);

            model.AxisMappings = axisMappings
                .Select(x => new AkeneoFamilyVariantAxisMappingModel
                {
                    Id = x.Id,
                    FamilyVariantImportConfigurationId = x.FamilyVariantImportConfigurationId,
                    AkeneoAttributeCode = x.AkeneoAttributeCode,
                    NopProductAttributeId = x.NopProductAttributeId,
                    IsRequired = x.IsRequired,
                    DisplayOrder = x.DisplayOrder
                })
                .ToList();
        }

        await PrepareSelectListsAsync(model);

        return model;
    }

    private static IList<SelectListItem> BuildModeList() =>
        new List<SelectListItem>
        {
            new("None", ((int)AkeneoVariantRelationshipMode.None).ToString()),
            new("Product attribute combinations", ((int)AkeneoVariantRelationshipMode.ProductAttributeCombinations).ToString()),
            new("Associated-to-product product attribute values", ((int)AkeneoVariantRelationshipMode.AssociatedToProductAttributeValue).ToString()),
            new("Grouped products", ((int)AkeneoVariantRelationshipMode.GroupedProducts).ToString())
        };


    private async Task PrepareSelectListsAsync(AkeneoFamilyVariantImportConfigurationModel model)
    {
        model.AvailableVariantRelationshipModes = BuildModeList();   // unchanged

        var productAttributes = await _productAttributeService.GetAllProductAttributesAsync();
        model.AvailableProductAttributes = productAttributes
            .Select(x => new SelectListItem(x.Name, x.Id.ToString()))
            .ToList();
        model.AvailableProductAttributes.Insert(0, new SelectListItem("Select product attribute", string.Empty));

        // Families → dropdown instead of free text.
        var families = await _akeneoApiClient.GetFamiliesAsync();
        model.AvailableAkeneoFamilies = families
            .Select(f => new SelectListItem(
                f.GetLabel() == f.Code ? f.Code : $"{f.GetLabel()} ({f.Code})",
                f.Code))
            .ToList();
        model.AvailableAkeneoFamilies.Insert(0, new SelectListItem("Select Akeneo family", string.Empty));

        // Axis attributes scoped to the chosen family (edit case, or post-back with a family set).
        if (!string.IsNullOrWhiteSpace(model.AkeneoFamilyCode))
        {
            var axes = await _akeneoApiClient.GetFamilyVariantAxesAsync(model.AkeneoFamilyCode);
            model.AvailableAkeneoAttributes = axes
                .Select(a => new SelectListItem($"{a.AttributeCode} (level {a.Level})", a.AttributeCode))
                .ToList();
            //model.AvailableAkeneoLevelCount = axes.Count == 0 ? 1 : axes.Max(a => a.Level);
        }
        else
        {
            model.AvailableAkeneoAttributes = new List<SelectListItem>();
        }
    }
}