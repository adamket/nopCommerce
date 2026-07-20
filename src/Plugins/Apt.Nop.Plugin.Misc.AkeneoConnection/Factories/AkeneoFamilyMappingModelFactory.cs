using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;

public class AkeneoFamilyMappingModelFactory
    : IAkeneoFamilyMappingModelFactory
{
    private readonly IAkeneoFamilyMappingService _configurationService;
    private readonly IAkeneoApiClient _akeneoApiClient;
    private readonly IProductAttributeService _productAttributeService;

    public AkeneoFamilyMappingModelFactory(
        IAkeneoFamilyMappingService configurationService,
        IAkeneoApiClient akeneoApiClient,
        IProductAttributeService productAttributeService)
    {
        _configurationService = configurationService;
        _akeneoApiClient = akeneoApiClient;
        _productAttributeService = productAttributeService;
    }

    public async Task<AkeneoFamilyMappingListModel> PrepareListModelAsync()
    {
        var configurations = await _configurationService.GetAllAsync();

        var model = new AkeneoFamilyMappingListModel
        {
            Configurations = configurations
                .Select(x => new AkeneoFamilyMappingModel
                {
                    Id = x.Id,
                    AkeneoFamilyCode = x.AkeneoFamilyCode,
                    Enabled = x.Enabled,
                    VariantRelationshipModeId = x.VariantRelationshipModeId,
                    ProductModelHierarchyModeId = x.ProductModelHierarchyModeId,
                    PreserveExistingNopVariantStructure = x.PreserveExistingNopVariantStructure,
                    AssociatedProductAttributeId = x.AssociatedProductAttributeId,
                    AssociatedValueNameTemplate = x.AssociatedValueNameTemplate,
                    HideChildProductsWhenRepresentedByParent = x.HideChildProductsWhenRepresentedByParent,
                    DisplayOrder = x.DisplayOrder
                })
                .ToList()
        };

        return model;
    }

    public async Task<AkeneoFamilyMappingModel> PrepareModelAsync(
        AkeneoFamilyMappingModel model = null,
        AkeneoFamilyMapping configuration = null)
    {
        // A null model means this is the initial GET and persisted values should be loaded.
        // A non-null model is a POST-back and its submitted rows must be preserved.
        var loadPersistedValues = model == null;
        model ??= new AkeneoFamilyMappingModel();

        if (configuration != null && loadPersistedValues)
        {
            model.Id = configuration.Id;
            model.AkeneoFamilyCode = configuration.AkeneoFamilyCode;
            model.Enabled = configuration.Enabled;
            model.VariantRelationshipModeId = configuration.VariantRelationshipModeId;
            model.ProductModelHierarchyModeId = configuration.ProductModelHierarchyModeId;
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

            var subModelRules = await _configurationService.GetSubModelRulesAsync(configuration.Id);

            model.SubModelRules = subModelRules
                .Select(x => new AkeneoFamilySubModelRuleModel
                {
                    Id = x.Id,
                    FamilyMappingId = x.FamilyMappingId,
                    AkeneoAxisAttributeCode = x.AkeneoAxisAttributeCode,
                    TriggerValue = x.TriggerValue,
                    VariantAxisAttributeCode = x.VariantAxisAttributeCode,
                    VariantTriggerValue = x.VariantTriggerValue,
                    VariantRelationshipOverrideModeId = x.VariantRelationshipOverrideModeId,
                    MergeAncestorValues = x.MergeAncestorValues,
                    DisplayOrder = x.DisplayOrder
                })
                .ToList();
        }

        model.AxisMappings ??= new List<AkeneoFamilyVariantAxisMappingModel>();
        model.SubModelRules ??= new List<AkeneoFamilySubModelRuleModel>();

        await PrepareSelectListsAsync(model);

        return model;
    }

    private static IList<SelectListItem> BuildModeList() =>
        new List<SelectListItem>
        {
            new("None / import as standalone product", ((int)AkeneoVariantRelationshipMode.None).ToString()),
            new("Product attribute combinations", ((int)AkeneoVariantRelationshipMode.ProductAttributeCombinations).ToString()),
            new("Associated-to-product product attribute values", ((int)AkeneoVariantRelationshipMode.AssociatedToProductAttributeValue).ToString()),
            new("Grouped products", ((int)AkeneoVariantRelationshipMode.GroupedProducts).ToString())
        };

    private static IList<SelectListItem> BuildHierarchyModeList() =>
        new List<SelectListItem>
        {
            new(
                "Immediate parent product model",
                ((int)AkeneoProductModelHierarchyMode.ImmediateParentProductModel).ToString()),
            new(
                "Root product model / flatten submodels",
                ((int)AkeneoProductModelHierarchyMode.RootProductModel).ToString())
        };

    private async Task PrepareSelectListsAsync(AkeneoFamilyMappingModel model)
    {
        model.AvailableVariantRelationshipModes = BuildModeList();
        model.AvailableProductModelHierarchyModes = BuildHierarchyModeList();

        var productAttributes = await _productAttributeService.GetAllProductAttributesAsync();
        model.AvailableProductAttributes = productAttributes
            .Select(x => new SelectListItem(x.Name, x.Id.ToString()))
            .ToList();
        model.AvailableProductAttributes.Insert(0, new SelectListItem("Select product attribute", string.Empty));

        var families = await _akeneoApiClient.GetFamiliesAsync();
        model.AvailableAkeneoFamilies = families
            .Select(f => new SelectListItem(
                f.GetLabel() == f.Code ? f.Code : $"{f.GetLabel()} ({f.Code})",
                f.Code))
            .ToList();
        model.AvailableAkeneoFamilies.Insert(0, new SelectListItem("Select Akeneo family", string.Empty));

        model.AvailableAkeneoAttributes = new List<SelectListItem>();
        model.AvailableSubModelAxisAttributes = new List<SelectListItem>
        {
            new("No sub-model condition", string.Empty)
        };
        model.AvailableVariantAxisAttributes = new List<SelectListItem>
        {
            new("No variant condition", string.Empty)
        };

        if (string.IsNullOrWhiteSpace(model.AkeneoFamilyCode))
            return;

        var axes = await _akeneoApiClient.GetFamilyVariantAxesAsync(model.AkeneoFamilyCode);

        model.AvailableAkeneoAttributes = axes
            .Select(a => new SelectListItem($"{a.AttributeCode} (level {a.Level})", a.AttributeCode))
            .ToList();

        foreach (var axis in axes.Where(x => x.Level == 1))
        {
            model.AvailableSubModelAxisAttributes.Add(
                new SelectListItem($"{axis.AttributeCode} (level {axis.Level})", axis.AttributeCode));
        }

        foreach (var axis in axes.Where(x => x.Level >= 1))
        {
            model.AvailableVariantAxisAttributes.Add(
                new SelectListItem($"{axis.AttributeCode} (level {axis.Level})", axis.AttributeCode));
        }

        // Preserve a saved or posted code even if Akeneo no longer reports it as an axis.
        foreach (var code in model.SubModelRules
                     .Select(x => x.AkeneoAxisAttributeCode)
                     .Where(x => !string.IsNullOrWhiteSpace(x))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            AddMissingOption(model.AvailableSubModelAxisAttributes, code);
        }

        foreach (var code in model.SubModelRules
                     .Select(x => x.VariantAxisAttributeCode)
                     .Where(x => !string.IsNullOrWhiteSpace(x))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            AddMissingOption(model.AvailableVariantAxisAttributes, code);
        }
    }

    private static void AddMissingOption(IList<SelectListItem> options, string code)
    {
        if (options.Any(x => string.Equals(x.Value, code, StringComparison.OrdinalIgnoreCase)))
            return;

        options.Add(new SelectListItem($"{code} (not currently returned by Akeneo)", code));
    }
}
