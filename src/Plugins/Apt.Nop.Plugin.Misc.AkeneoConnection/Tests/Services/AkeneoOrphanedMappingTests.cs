using Apt.Nop.Plugin.Misc.AkeneoConnection.Controllers;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Services.Catalog;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Messages;
using NUnit.Framework;
using System.Text.Json;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

[TestFixture]
public class AkeneoOrphanedMappingTests
{
    [TestCase(null, null, false)]
    [TestCase("trees", null, true)]
    [TestCase("trees", "trees", false)]
    public async Task Deleted_attribute_is_visible_with_saved_identity_and_excluded_from_source_picker(
        string? selectedFamily, string? mappingFamily, bool inherited)
    {
        var mapping = new AkeneoAttributeMapping
        {
            Id = 42, MappingKey = "removed_attribute", AkeneoAttributeCode = "removed_attribute",
            AkeneoFamilyCode = mappingFamily!, NopTargetTypeId = (int)NopTargetType.ProductField, NopTargetKey = "Published"
        };
        var api = new Mock<IAkeneoApiClient>(MockBehavior.Strict);
        api.Setup(x => x.GetAttributesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AkeneoAttributeDefinition>());
        api.Setup(x => x.GetFamiliesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AkeneoFamilyDefinition>());
        api.Setup(x => x.GetFamilyByCodeAsync("trees", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AkeneoFamilyDefinition { Code = "trees", Attributes = new List<string>() });
        var mappings = new Mock<IAkeneoAttributeMappingService>();
        mappings.Setup(x => x.GetEffectiveMappingsAsync(It.IsAny<string>())).ReturnsAsync(new List<AkeneoAttributeMapping> { mapping });
        mappings.Setup(x => x.GetAllFallbackSourcesAsync()).ReturnsAsync(new List<AkeneoAttributeMappingFallbackSource>());
        var specs = new Mock<ISpecificationAttributeService>();
        specs.Setup(x => x.GetAllSpecificationAttributesAsync(0, int.MaxValue))
            .ReturnsAsync(new PagedList<SpecificationAttribute>(new List<SpecificationAttribute>(), 0, int.MaxValue));
        var attributes = new Mock<IProductAttributeService>();
        attributes.Setup(x => x.GetAllProductAttributesAsync(null, 0, int.MaxValue))
            .ReturnsAsync(new PagedList<ProductAttribute>(new List<ProductAttribute>(), 0, int.MaxValue));
        var customers = new Mock<ICustomerService>();
        customers.Setup(x => x.GetAllCustomerRolesAsync(true)).ReturnsAsync(new List<CustomerRole>());
        var factory = new AkeneoAttributeMappingModelFactory(api.Object, mappings.Object,
            Mock.Of<IAkeneoNopEntityMappingService>(), specs.Object, attributes.Object, new AkeneoTargetTypeResolver(), customers.Object);
        var model = await factory.PrepareAttributeMappingListModelAsync(selectedFamily!);
        var orphan = model.Mappings.Single(row => row.Id == 42);
        Assert.That(orphan.IsMissingSource, Is.True);
        Assert.That(orphan.IsInherited, Is.EqualTo(inherited));
        Assert.That(orphan.AkeneoAttributeGroupLabel, Is.EqualTo("Missing Akeneo attributes"));
        Assert.That(orphan.NopTargetKey, Is.EqualTo("Published"));
        Assert.That(orphan.AvailableTargetTypes.Any(option => option.Value == ((int)NopTargetType.ProductField).ToString()), Is.True);
        Assert.That(model.AvailableFallbackSources.Any(row => row.AkeneoAttributeCode == "removed_attribute"), Is.False);
    }

    [Test]
    public async Task Mapping_can_be_deleted_without_loading_missing_Akeneo_attribute()
    {
        var mapping = new AkeneoAttributeMapping { Id = 42, AkeneoAttributeCode = "removed_attribute", MappingKey = "removed_attribute" };
        var mappings = new Mock<IAkeneoAttributeMappingService>();
        mappings.Setup(x => x.GetAkeneoAttributeMappingByIdAsync(42)).ReturnsAsync(mapping);
        var api = new Mock<IAkeneoApiClient>(MockBehavior.Strict);
        var controller = new AkeneoMappingController(Mock.Of<IWorkContext>(), Mock.Of<IAkeneoAttributeMappingModelFactory>(),
            mappings.Object, api.Object, new AkeneoTargetTypeResolver(), Mock.Of<IAkeneoValueTemplateRenderer>(),
            Mock.Of<INotificationService>(), Mock.Of<IAkeneoNopEntityMappingService>(), Mock.Of<IStoreContext>(),
            Mock.Of<ISettingService>(), Mock.Of<IAkeneoCategoryMappingModelFactory>());
        var response = (JsonResult)await controller.DeleteAttributeMappingRow(42, null!, "removed_attribute", "removed_attribute");
        Assert.That(JsonSerializer.SerializeToElement(response.Value).GetProperty("success").GetBoolean(), Is.True);
        mappings.Verify(x => x.DeleteAkeneoAttributeMappingAsync(mapping), Times.Once);
        api.VerifyNoOtherCalls();
    }
}
