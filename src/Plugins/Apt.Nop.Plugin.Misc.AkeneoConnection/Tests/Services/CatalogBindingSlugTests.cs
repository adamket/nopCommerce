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

    private static IRepository<T> Repository<T>(params T[] items) where T : BaseEntity =>
        Mock.Of<IRepository<T>>(r => r.Table == items.AsQueryable());
}
