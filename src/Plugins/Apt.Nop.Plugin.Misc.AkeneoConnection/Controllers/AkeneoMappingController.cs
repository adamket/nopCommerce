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
public class AkeneoMappingController(
    IWorkContext workContext,
    IAkeneoAttributeMappingModelFactory attributeMappingModelFactory,
    IAkeneoAttributeMappingService attributeMappingService,
    INotificationService notificationService,
    IAkeneoNopEntityMappingService entityMappingService,
    IStoreContext storeContext,
    ISettingService settingService,
    IAkeneoCategoryMappingModelFactory categoryMappingModelFactory)
    : BasePluginController
{

    [HttpGet("admin/akeneo-connection/attribute-mappings")]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public async Task<IActionResult> AttributeMappings(string akeneoFamilyCode = null)
    {
        akeneoFamilyCode = string.IsNullOrWhiteSpace(akeneoFamilyCode)
            ? null
            : akeneoFamilyCode.Trim();

        var model = await attributeMappingModelFactory.PrepareAttributeMappingListModelAsync(akeneoFamilyCode);

        return View($"{AkeneoConnectionConstants.PathToPlugin}/Views/AttributeMappings.cshtml", model);
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

        var requestedFamilyCode =
            string.IsNullOrWhiteSpace(model.AkeneoFamilyCode)
                ? null
                : model.AkeneoFamilyCode.Trim();


        AkeneoAttributeMapping mapping = null;
        if (model.Id > 0)
        {
            mapping = await attributeMappingService
                .GetAkeneoAttributeMappingByIdAsync(model.Id);

            if (mapping == null)
            {
                return Json(new
                {
                    success = false,
                    errors = new[] { "The mapping could not be found." }
                });
            }

            var storedFamilyCode =
                string.IsNullOrWhiteSpace(mapping.AkeneoFamilyCode)
                    ? null
                    : mapping.AkeneoFamilyCode.Trim();

            if (!string.Equals(
                    storedFamilyCode,
                    requestedFamilyCode,
                    StringComparison.OrdinalIgnoreCase))
            {
                return Json(new
                {
                    success = false,
                    errors = new[]
                    {
                        "The mapping does not belong to the selected family scope."
                    }
                });
            }

            if (!string.Equals(
                    mapping.AkeneoAttributeCode,
                    model.AkeneoAttributeCode,
                    StringComparison.OrdinalIgnoreCase))
            {
                return Json(new
                {
                    success = false,
                    errors = new[]
                    {
                        "The mapping does not belong to the requested Akeneo attribute."
                    }
                });
            }
        }

        mapping ??= await attributeMappingService
            .GetAkeneoAttributeMappingByCodeAsync(
                model.AkeneoAttributeCode,
                requestedFamilyCode);

        var isNew = mapping == null;

        mapping ??= new AkeneoAttributeMapping
        {
            AkeneoAttributeCode = model.AkeneoAttributeCode
        };

        mapping.AkeneoAttributeTypeId = model.AkeneoAttributeTypeId;
        mapping.AkeneoFamilyCode = requestedFamilyCode;
        
        mapping.NopTargetTypeId = model.NopTargetTypeId;
        mapping.NopTargetKey = model.NopTargetKey;

        // Only persist the entity id for the target types that actually use one,
        // so switching away from spec/product doesn't leave a stale id behind.
        mapping.NopTargetEntityId =
            model.NopTargetTypeId == (int)NopTargetType.SpecificationAttribute ||
            model.NopTargetTypeId == (int)NopTargetType.ProductAttribute
                ? model.NopTargetEntityId
                : null;

       // var store = await storeContext.GetCurrentStoreAsync();
        var language = await workContext.GetWorkingLanguageAsync();
        mapping.Locale = language.LanguageCulture;
        //mapping.Channel = model.Channel;
        mapping.TransformRuleJson = model.TransformRuleJson;
        mapping.IsRequired = model.IsRequired;

        if (isNew)
            await attributeMappingService.InsertAkeneoAttributeMappingAsync(mapping);
        else
            await attributeMappingService.UpdateAkeneoAttributeMappingAsync(mapping);

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
            (!model.NopTargetEntityId.HasValue || model.NopTargetEntityId.Value <= 0))
        {
            errors.Add("Specification Attribute is required when Target Type is Specification Attribute.");
        }

        if (targetType == NopTargetType.ProductAttribute &&
            (!model.NopTargetEntityId.HasValue || model.NopTargetEntityId <= 0))
        {
            errors.Add("Product Attribute is required when Target Type is Product Attribute.");
        }

        return errors;
    }

    [HttpGet("admin/akeneo-connection/category-mappings")]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public async Task<IActionResult> CategoryMappings()
    {
        var model = await categoryMappingModelFactory.PrepareCategoryMappingListModelAsync();
        return View("~/Plugins/Apt.Misc.AkeneoConnection/Views/CategoryMappings.cshtml", model);
    }

    [HttpPost("admin/akeneo-connection/category-mappings")]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public async Task<IActionResult> SaveCategoryMapping(AkeneoCategoryMappingModel model)
    {
        

        if (string.IsNullOrWhiteSpace(model.AkeneoCode))
        {
            return Json(new
            {
                success = false,
                message = "Akeneo category code is required."
            });
        }

        if (model.NopCategoryId <= 0)
        {
            return Json(new
            {
                success = false,
                message = "Please select a nopCommerce category."
            });
        }

        await entityMappingService.UpsertAkeneoNopEntityMappingAsync(
            AkeneoEntityType.Category,
            model.AkeneoCode,
            null,
            NopEntityType.Category,
            model.NopCategoryId);

        return Json(new
        {
            success = true,
            message = "Category mapping saved."
        });
    }

    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    [HttpPost]
    public async Task<IActionResult> DeleteCategoryMapping(string akeneoCode)
    {
       

        if (string.IsNullOrWhiteSpace(akeneoCode))
        {
            return Json(new
            {
                success = false,
                message = "Akeneo category code is required."
            });
        }

        await entityMappingService.DeleteAkeneoNopEntityMappingAsync(
            AkeneoEntityType.Category,
            akeneoCode,
            NopEntityType.Category);

        return Json(new
        {
            success = true,
            message = "Category mapping cleared."
        });
    }

    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    [HttpPost]
    public async Task<IActionResult> DeleteAttributeMappingOverride(
        int id,
        string akeneoFamilyCode,
        string akeneoAttributeCode)
    {
        if (id <= 0 || string.IsNullOrWhiteSpace(akeneoFamilyCode))
        {
            return Json(new
            {
                success = false,
                errors = new[] { "A family override is required." }
            });
        }

        var mapping = await attributeMappingService
            .GetAkeneoAttributeMappingByIdAsync(id);

        if (mapping == null)
        {
            return Json(new
            {
                success = false,
                errors = new[] { "The mapping could not be found." }
            });
        }

        if (string.IsNullOrWhiteSpace(mapping.AkeneoFamilyCode) ||
            !string.Equals(
                mapping.AkeneoFamilyCode,
                akeneoFamilyCode,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                mapping.AkeneoAttributeCode,
                akeneoAttributeCode,
                StringComparison.OrdinalIgnoreCase))
        {
            return Json(new
            {
                success = false,
                errors = new[]
                {
                    "The requested mapping is not a family override."
                }
            });
        }

        await attributeMappingService
            .DeleteAkeneoAttributeMappingAsync(mapping);

        return Json(new
        {
            success = true
        });
    }
}
