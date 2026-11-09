using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Seo;
using Nop.Data;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

[TestFixture]
public class CatalogBindingSlugTests
{
    [TestCase("red-azaleas-2", true, true)]
    [TestCase("red-azaleas", true, false)]
    [TestCase("red-azaleas-2", false, false)]
    public async Task MissingVariantSkuOffersOnlyAnExactActiveSlugForReview(string slug, bool active, bool expected)
    {
        var model = new AkeneoProductDefinition
        {
            Code = "red_azalea", Family = "nursery", FamilyVariant = "sizes",
            Values = JsonSerializer.SerializeToElement(new { url_slug = new[] { new { locale = "en_US", data = "red-azaleas-2" } } })
        };
        var leaf = new AkeneoProductDefinition { Identifier = "10645", Parent = model.Code, Family = model.Family };
        var api = new Mock<IAkeneoApiClient>();
        api.Setup(s => s.GetProductModelsPageAsync(100, null, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AkeneoProductPageResult { Items = new[] { model } });
        api.Setup(s => s.GetProductsPageAsync(100, null, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AkeneoProductPageResult { Items = new[] { leaf } });
        api.Setup(s => s.GetCategoriesAsync(100, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<AkeneoCategoryDefinition>());
        var mappings = new Mock<IAkeneoNopEntityMappingService>();
        mappings.Setup(s => s.GetAkeneoNopEntityMappingsAsync(It.IsAny<AkeneoEntityType?>()))
            .ReturnsAsync(Array.Empty<AkeneoNopEntityMapping>());
        var service = new AkeneoCatalogBindingService(api.Object, new AkeneoProductValueResolver(), mappings.Object,
            Mock.Of<IAkeneoFamilyMappingService>(), new AkeneoProductModelHierarchyResolver(), null, null, null,
            Repository(new Product { Id = 1151, Name = "Red Azalea" }), Repository<ProductAttributeCombination>(),
            Repository<ProductAttributeMapping>(), Repository<ProductAttributeValue>(), Repository<Category>(),
            Repository(new UrlRecord { Slug = slug, EntityName = nameof(Product), EntityId = 1151, IsActive = active }),
            null, null, null, null);

        var report = await service.ScanAsync(new[] { "nursery" });

        Assert.That(report.Models.Matched, Is.Zero);
        Assert.That(report.Conflicts.Count, Is.EqualTo(expected ? 1 : 0));
        if (expected)
        {
            var candidate = report.Conflicts.Single().Candidates.Single();
            Assert.That(candidate.ProductId, Is.EqualTo(1151));
            Assert.That(candidate.MatchedSlug, Is.EqualTo(slug));
            Assert.That(candidate.VariantVotes, Is.Zero);
        }
    }

    [Test]
    public async Task Rebind_reviews_then_updates_only_the_expected_parent()
    {
        const string code = "nursery_dwarf_mugo_pine";
        var binding = new AkeneoNopEntityMapping { Id = 12, AkeneoCode = code, NopEntityId = 1141 };
        var mappings = new Mock<IAkeneoNopEntityMappingService>();
        mappings.Setup(s => s.GetAkeneoNopEntityMappingByAkeneoCodeAsync(AkeneoEntityType.ProductModel, code, NopEntityType.Product))
            .ReturnsAsync(binding);
        mappings.Setup(s => s.GetMappingsByNopEntityAsync(NopEntityType.Product, 1404))
            .ReturnsAsync(Array.Empty<AkeneoNopEntityMapping>());
        var leases = new Mock<IAkeneoSyncLeaseService>();
        leases.Setup(s => s.TryAcquireAsync(AkeneoConnectionConstants.CatalogWriterLockKey, It.IsAny<TimeSpan>()))
            .ReturnsAsync(new AkeneoSyncLease());
        var service = new AkeneoCatalogBindingService(null, null, mappings.Object, null, null, leases.Object, null, null,
            Repository(new Product { Id = 1141, Name = "Mugo Pine" }, new Product { Id = 1404, Name = "Dwarf Mugo Pine" }),
            null, null, null, null, null, null, null, null, null);

        var review = await service.RebindAsync(code, 1404, null);
        Assert.That(review, Is.EqualTo((1141, "Mugo Pine", "Dwarf Mugo Pine")));
        Assert.ThrowsAsync<InvalidOperationException>(() => service.RebindAsync(code, 1404, 99));
        mappings.Verify(s => s.UpdateAkeneoNopEntityMappingAsync(It.IsAny<AkeneoNopEntityMapping>()), Times.Never);

        await service.RebindAsync(code, 1404, 1141);
        Assert.That(binding.NopEntityId, Is.EqualTo(1404));
        mappings.Verify(s => s.UpdateAkeneoNopEntityMappingAsync(binding), Times.Once);
    }

    private static IRepository<T> Repository<T>(params T[] items) where T : BaseEntity =>
        Mock.Of<IRepository<T>>(r => r.Table == items.AsQueryable());
}
