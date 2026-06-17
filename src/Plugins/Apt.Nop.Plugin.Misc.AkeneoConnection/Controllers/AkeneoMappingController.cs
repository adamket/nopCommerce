using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Services.Configuration;
using Nop.Services.Messages;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Controllers;

[AuthorizeAdmin]
[Area(AreaNames.ADMIN)]
[AutoValidateAntiforgeryToken]
public class AkeneoMappingController : BasePluginController
{

    private readonly IAkeneoMappingModelFactory _mappingModelFactory;
    private readonly IAkeneoAttributeMappingService _akeneoAttributeMappingService;
    private readonly INotificationService _notificationService;
    private readonly IAkeneoNopEntityMappingService _entityMappingService;
    private readonly IStoreContext _storeContext;
    private readonly ISettingService _settingService;

    public AkeneoMappingController(
        IAkeneoMappingModelFactory mappingModelFactory,
        IAkeneoAttributeMappingService akeneoAttributeMappingService,
        INotificationService notificationService, IAkeneoNopEntityMappingService entityMappingService, IStoreContext storeContext, ISettingService settingService)
    {
        _mappingModelFactory = mappingModelFactory;
        _akeneoAttributeMappingService = akeneoAttributeMappingService;
        _notificationService = notificationService;
        _entityMappingService = entityMappingService;
        _storeContext = storeContext;
        _settingService = settingService;
    }

    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public async Task<IActionResult> AttributeMappings()
    {
        var model = await _mappingModelFactory.PrepareAttributeMappingListModelAsync();

        return View($"{AkeneoConstants.PathToPlugin}/Views/AttributeMappings.cshtml", model);
    }

    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    [HttpPost]
    public async Task<IActionResult> SaveAttributeMappingRow(
        AkeneoAttributeMappingModel model)
    {
        var errors = ValidateAttributeMappingRow(model);

        if (errors.Any())
        {
            return Json(new
            {
                success = false,
                errors
            });
        }

        var mapping = model.Id > 0
            ? await _akeneoAttributeMappingService.GetAkeneoAttributeMappingByIdAsync(model.Id)
            : null;

        mapping ??= await _akeneoAttributeMappingService
            .GetAkeneoAttributeMappingByCodeAsync(model.AkeneoAttributeCode);

        var isNew = mapping == null;

        mapping ??= new AkeneoAttributeMapping
        {
            AkeneoAttributeCode = model.AkeneoAttributeCode
        };

        mapping.AkeneoAttributeTypeId = model.AkeneoAttributeTypeId;
        mapping.NopTargetTypeId = model.NopTargetTypeId;
        mapping.NopTargetKey = model.NopTargetKey;
        mapping.Locale = model.Locale;
        mapping.Channel = model.Channel;
        mapping.TransformRuleJson = model.TransformRuleJson;
        mapping.IsRequired = model.IsRequired;

        if (isNew)
            await _akeneoAttributeMappingService.InsertAkeneoAttributeMappingAsync(mapping);
        else
            await _akeneoAttributeMappingService.UpdateAkeneoAttributeMappingAsync(mapping);

        await SaveRelatedNopEntityMappingsAsync(model);

        return Json(new
        {
            success = true,
            id = mapping.Id,
            message = "Mapping saved."
        });
    }

    private static IList<string> ValidateAttributeMappingRow(
        AkeneoAttributeMappingModel model)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(model.AkeneoAttributeCode))
            errors.Add("Akeneo attribute code is required.");

        if (!Enum.IsDefined(typeof(NopTargetType), model.NopTargetTypeId))
            errors.Add("Invalid nopCommerce target type.");

        if (!string.IsNullOrWhiteSpace(model.TransformRuleJson))
        {
            try
            {
                JsonDocument.Parse(model.TransformRuleJson);
            }
            catch (JsonException)
            {
                errors.Add("Transform Rule JSON is not valid JSON.");
            }
        }

        var targetType = (NopTargetType)model.NopTargetTypeId;

        if ((targetType == NopTargetType.ProductField ||
             targetType == NopTargetType.SeoField ||
             targetType == NopTargetType.CustomProperty) &&
            string.IsNullOrWhiteSpace(model.NopTargetKey))
        {
            errors.Add("Target Key is required for the selected target type.");
        }

        if (targetType == NopTargetType.SpecificationAttribute &&
            (!model.NopSpecificationAttributeId.HasValue || model.NopSpecificationAttributeId.Value <= 0))
        {
            errors.Add("Specification Attribute is required when Target Type is Specification Attribute.");
        }

        if (targetType == NopTargetType.ProductAttribute &&
            (!model.NopProductAttributeId.HasValue || model.NopProductAttributeId.Value <= 0))
        {
            errors.Add("Product Attribute is required when Target Type is Product Attribute.");
        }

        return errors;
    }

    private async Task SaveRelatedNopEntityMappingsAsync(
        AkeneoAttributeMappingModel model)
    {
        var targetType = (NopTargetType)model.NopTargetTypeId;

        if (targetType == NopTargetType.SpecificationAttribute)
        {
            await _entityMappingService.UpsertAkeneoNopEntityMappingAsync(
                akeneoEntityType: AkeneoEntityType.Attribute,
                akeneoCode: model.AkeneoAttributeCode,
                nopEntityType: NopEntityType.SpecificationAttribute,
                nopEntityId: model.NopSpecificationAttributeId.Value);

            await _entityMappingService.DeleteAkeneoNopEntityMappingAsync(
                akeneoEntityType: AkeneoEntityType.Attribute,
                akeneoCode: model.AkeneoAttributeCode,
                nopEntityType: NopEntityType.ProductAttribute);

            return;
        }

        if (targetType == NopTargetType.ProductAttribute)
        {
            await _entityMappingService.UpsertAkeneoNopEntityMappingAsync(
                akeneoEntityType: AkeneoEntityType.Attribute,
                akeneoCode: model.AkeneoAttributeCode,
                nopEntityType: NopEntityType.ProductAttribute,
                nopEntityId: model.NopProductAttributeId.Value);

            await _entityMappingService.DeleteAkeneoNopEntityMappingAsync(
                akeneoEntityType: AkeneoEntityType.Attribute,
                akeneoCode: model.AkeneoAttributeCode,
                nopEntityType: NopEntityType.SpecificationAttribute);

            return;
        }

        await _entityMappingService.DeleteAkeneoNopEntityMappingAsync(
            akeneoEntityType: AkeneoEntityType.Attribute,
            akeneoCode: model.AkeneoAttributeCode,
            nopEntityType: NopEntityType.SpecificationAttribute);

        await _entityMappingService.DeleteAkeneoNopEntityMappingAsync(
            akeneoEntityType: AkeneoEntityType.Attribute,
            akeneoCode: model.AkeneoAttributeCode,
            nopEntityType: NopEntityType.ProductAttribute);
    }
}
