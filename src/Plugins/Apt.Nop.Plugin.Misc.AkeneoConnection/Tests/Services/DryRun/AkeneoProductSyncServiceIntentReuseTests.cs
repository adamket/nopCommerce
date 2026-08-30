using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Moq;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;
using NUnit.Framework;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

[TestFixture]
public class AkeneoProductSyncServiceIntentReuseTests
{
    [Test]
    public async Task Resolved_intent_is_reused_while_destination_binding_is_refreshed()
    {
        var attributeMappings = new Mock<IAkeneoAttributeMappingService>();
        attributeMappings
            .Setup(service => service.GetEffectiveMappingsAsync(It.IsAny<string>()))
            .ReturnsAsync(new List<AkeneoAttributeMapping>());
        attributeMappings
            .Setup(service => service.GetAllFallbackSourcesAsync())
            .ReturnsAsync(new List<AkeneoAttributeMappingFallbackSource>());

        var assetMappings = new Mock<IAkeneoAssetMappingService>();
        assetMappings
            .Setup(service => service.GetEffectiveMappingsAsync(It.IsAny<string>()))
            .ReturnsAsync(new List<AkeneoAssetMapping>());

        var entityMappings = new Mock<IAkeneoNopEntityMappingService>();
        entityMappings
            .Setup(service => service.GetMappedNopEntityIdByAkeneoUuidAsync(
                AkeneoEntityType.Product,
                "uuid-1",
                NopEntityType.Product))
            .ReturnsAsync((int?)null);
        entityMappings
            .Setup(service => service.GetMappedNopEntityIdByAkeneoCodeAsync(
                AkeneoEntityType.Product,
                "SKU-1",
                NopEntityType.Product))
            .ReturnsAsync((int?)null);

        var productService = new Mock<IProductService>();
        productService
            .SetupSequence(service => service.GetProductBySkuAsync("SKU-1"))
            .ReturnsAsync(new Product { Id = 101, Sku = "SKU-1" })
            .ReturnsAsync(new Product { Id = 202, Sku = "SKU-1" });

        var service = new AkeneoProductSyncService(
            new Mock<IAkeneoProductValueResolver>().Object,
            new Mock<IAkeneoReferenceEntityValueResolver>().Object,
            new Mock<IAkeneoValueTransformationService>().Object,
            new Mock<IAkeneoValueTemplateRenderer>().Object,
            attributeMappings.Object,
            assetMappings.Object,
            entityMappings.Object,
            new Mock<IAkeneoProductSyncStateService>().Object,
            productService.Object,
            new Mock<IAkeneoProductSyncPipeline>().Object);

        var source = new AkeneoProductDefinition
        {
            Uuid = "uuid-1",
            Identifier = "SKU-1",
            Family = "family",
            Enabled = true,
            Categories = new List<string>(),
            Values = JsonSerializer.SerializeToElement(new { })
        };
        var request = new AkeneoProductImportRequest
        {
            SyncRunRecordId = 1,
            Locale = "en_US",
            Channel = "ecommerce",
            Currency = "USD"
        };

        var intent = await service.ResolveIntentAsync(
            source,
            AkeneoEntityType.Product,
            request);

        var firstContext = await service.PrepareFromIntentAsync(
            intent,
            new AkeneoProductImportResult());
        var secondContext = await service.PrepareFromIntentAsync(
            intent,
            new AkeneoProductImportResult());

        Assert.Multiple(() =>
        {
            Assert.That(firstContext.ExistingProduct?.Id, Is.EqualTo(101));
            Assert.That(secondContext.ExistingProduct?.Id, Is.EqualTo(202));
        });

        // Source-side mapping/configuration work occurred once, when the intent
        // was created. Rebinding the intent did not repeat it.
        attributeMappings.Verify(
            x => x.GetEffectiveMappingsAsync("family"),
            Times.Once);
        attributeMappings.Verify(
            x => x.GetAllFallbackSourcesAsync(),
            Times.Once);
        assetMappings.Verify(
            x => x.GetEffectiveMappingsAsync("family"),
            Times.Once);

        // Destination lookup is intentionally fresh on each binding.
        productService.Verify(
            x => x.GetProductBySkuAsync("SKU-1"),
            Times.Exactly(2));
    }
}
