using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Nop.Core;
using Nop.Core.Domain.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

[TestFixture]
public class AkeneoVariantRelationshipResolverTests
{
    private Mock<INopVariantStructureDetector> _detector = null!;
    private Mock<IAkeneoFamilyMappingService> _familyService = null!;
    private AkeneoVariantRelationshipResolver _resolver = null!;

    [SetUp]
    public void SetUp()
    {
        _detector = new Mock<INopVariantStructureDetector>(MockBehavior.Strict);
        _familyService = new Mock<IAkeneoFamilyMappingService>(MockBehavior.Strict);
        _resolver = new AkeneoVariantRelationshipResolver(_detector.Object, _familyService.Object);
    }

    [Test]
    public async Task ResolveAsync_preserves_detected_existing_structure_when_configured()
    {
        var parent = new Product { Id = 12 };
        var familyOptions = Options(
            AkeneoVariantRelationshipMode.GroupedProducts,
            preserveExisting: true);
        _familyService
            .Setup(service => service.BuildOptionsForFamilyAsync("trees"))
            .ReturnsAsync(familyOptions);
        _detector
            .Setup(detector => detector.DetectAsync(parent))
            .ReturnsAsync(new NopVariantStructureDetectionResult
            {
                HasVariantStructure = true,
                Mode = AkeneoVariantRelationshipMode.AssociatedToProductAttributeValue,
                AssociatedProductAttributeId = 55
            });

        var result = await _resolver.ResolveAsync(parent, "trees");

        Assert.Multiple(() =>
        {
            Assert.That(result.Mode, Is.EqualTo(AkeneoVariantRelationshipMode.AssociatedToProductAttributeValue));
            Assert.That(result.Source, Is.EqualTo(AkeneoVariantRelationshipSource.ExistingNopParent));
            Assert.That(result.Options.AssociatedProductAttributeId, Is.EqualTo(55));
        });
    }

    [Test]
    public void ResolveAsync_rejects_ambiguous_existing_structure()
    {
        var parent = new Product { Id = 12 };
        _familyService
            .Setup(service => service.BuildOptionsForFamilyAsync("trees"))
            .ReturnsAsync(Options(AkeneoVariantRelationshipMode.GroupedProducts, true));
        _detector
            .Setup(detector => detector.DetectAsync(parent))
            .ReturnsAsync(new NopVariantStructureDetectionResult { IsAmbiguous = true });

        Assert.ThrowsAsync<NopException>(
            async () => await _resolver.ResolveAsync(parent, "trees"));
    }

    [Test]
    public async Task ResolveAsync_uses_family_configuration_when_no_existing_structure_exists()
    {
        var parent = new Product { Id = 12 };
        var familyOptions = Options(
            AkeneoVariantRelationshipMode.ProductAttributeCombinations,
            preserveExisting: true);
        _familyService
            .Setup(service => service.BuildOptionsForFamilyAsync("trees"))
            .ReturnsAsync(familyOptions);
        _detector
            .Setup(detector => detector.DetectAsync(parent))
            .ReturnsAsync(new NopVariantStructureDetectionResult());

        var result = await _resolver.ResolveAsync(parent, "trees");

        Assert.Multiple(() =>
        {
            Assert.That(result.Mode, Is.EqualTo(AkeneoVariantRelationshipMode.ProductAttributeCombinations));
            Assert.That(result.Source, Is.EqualTo(AkeneoVariantRelationshipSource.AkeneoFamilyConfiguration));
            Assert.That(result.Options, Is.SameAs(familyOptions));
        });
    }

    [Test]
    public async Task ResolveAsync_skips_existing_structure_detection_when_preservation_is_disabled()
    {
        var parent = new Product { Id = 12 };
        var familyOptions = Options(
            AkeneoVariantRelationshipMode.GroupedProducts,
            preserveExisting: false);
        _familyService
            .Setup(service => service.BuildOptionsForFamilyAsync("trees"))
            .ReturnsAsync(familyOptions);

        var result = await _resolver.ResolveAsync(parent, "trees");

        Assert.That(result.Mode, Is.EqualTo(AkeneoVariantRelationshipMode.GroupedProducts));
        _detector.Verify(detector => detector.DetectAsync(It.IsAny<Product>()), Times.Never);
    }

    [Test]
    public void ResolveAsync_requires_family_configuration_when_structure_cannot_be_preserved()
    {
        var parent = new Product { Id = 12 };
        _familyService
            .Setup(service => service.BuildOptionsForFamilyAsync("trees"))
            .ReturnsAsync((AkeneoVariantRelationshipOptions?)null);
        _detector
            .Setup(detector => detector.DetectAsync(parent))
            .ReturnsAsync(new NopVariantStructureDetectionResult());

        Assert.ThrowsAsync<NopException>(
            async () => await _resolver.ResolveAsync(parent, "trees"));
    }

    private static AkeneoVariantRelationshipOptions Options(
        AkeneoVariantRelationshipMode mode,
        bool preserveExisting) => new()
        {
            Enabled = true,
            AkeneoFamilyCode = "trees",
            Mode = mode,
            PreserveExistingNopVariantStructure = preserveExisting
        };
}
