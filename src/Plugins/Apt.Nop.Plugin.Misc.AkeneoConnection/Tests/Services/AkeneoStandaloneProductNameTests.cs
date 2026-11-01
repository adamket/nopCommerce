using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core.Domain.Catalog;
using Nop.Data;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

[TestFixture]
public class AkeneoStandaloneProductNameTests
{
    private static readonly AkeneoValueTemplateRenderer Renderer = new(new AkeneoProductValueResolver());

    [TestCase(false, "Red Maple Seedling", "Red Maple Seedling")]
    [TestCase(true, "Red Maple Seedling", "Red Maple Seedling")]
    [TestCase(false, null, "Normal mapped name")]
    [TestCase(true, null, "Normal mapped name")]
    [TestCase(false, "", "Normal mapped name")]
    [TestCase(true, "  ", "Normal mapped name")]
    public async Task A_seedling_name_overrides_only_when_populated_and_matches_dry_run(bool existing, string? supplied, string expected)
    {
        var context = Context(supplied, existing);
        await AkeneoStandaloneProductOverrides.ApplyAsync(context, Rule(), Renderer);
        var products = new Mock<IProductService>();
        products.Setup(service => service.InsertProductAsync(It.IsAny<Product>())).Returns(Task.CompletedTask);
        var synchronizer = new AkeneoProductCoreSynchronizer(products.Object, Mock.Of<IRepository<Product>>());
        var preview = new AkeneoProductMappingPreviewModel();
        await synchronizer.PlanAsync(context, preview);
        var name = preview.Operations.Single(operation => operation.Area == "Product fields" && operation.Target == "Name");
        Assert.That(name.ProposedValue, Is.EqualTo(expected));
        Assert.That(context.PlanningState.ProposedProductName, Is.EqualTo(expected), "Downstream name-dependent plans use the same name.");
        if (existing)
            Assert.That(context.ExistingProduct.Name, Is.EqualTo("Old name"), "Dry Run must not write.");
        await synchronizer.SynchronizeAsync(context);
        Assert.That(context.Product.Name, Is.EqualTo(expected));
    }

    [TestCase(AkeneoVariantRelationshipMode.AssociatedToProductAttributeValue, AkeneoSubModelParentLevel.FamilySetting)]
    [TestCase(AkeneoVariantRelationshipMode.GroupedProducts, AkeneoSubModelParentLevel.FamilySetting)]
    [TestCase(AkeneoVariantRelationshipMode.ProductAttributeCombinations, AkeneoSubModelParentLevel.FamilySetting)]
    [TestCase(AkeneoVariantRelationshipMode.None, AkeneoSubModelParentLevel.MatchingSubModel)]
    public async Task Related_variants_and_submodel_parents_do_not_receive_the_name_override(
        AkeneoVariantRelationshipMode mode, AkeneoSubModelParentLevel parent)
    {
        var rule = Rule();
        rule.VariantRelationshipOverrideMode = mode;
        rule.ParentLevel = parent;
        var context = Context("Seedling name", true);
        await AkeneoStandaloneProductOverrides.ApplyAsync(context, rule, Renderer);
        Assert.That(context.StandaloneProductNameOverride, Is.Null);
    }

    [Test]
    public async Task An_unconfigured_rule_keeps_normal_naming()
    {
        var rule = Rule();
        rule.StandaloneProductNameTemplate = null;
        var context = Context("Seedling name", true);
        await AkeneoStandaloneProductOverrides.ApplyAsync(context, rule, Renderer);
        Assert.That(context.StandaloneProductNameOverride, Is.Null);
    }

    [Test]
    public async Task The_profile_locale_and_channel_select_the_override_value()
    {
        var context = Context("unused", true);
        context.Source.Values = JsonSerializer.SerializeToElement(new
        {
            root_variant_name = new[]
            {
                new { data = "French name", locale = "fr_FR", scope = "ecommerce" },
                new { data = "Print name", locale = "en_US", scope = "print" },
                new { data = "Website seedling name", locale = "en_US", scope = "ecommerce" }
            }
        });
        await AkeneoStandaloneProductOverrides.ApplyAsync(context, Rule(), Renderer);
        Assert.That(context.StandaloneProductNameOverride, Is.EqualTo("Website seedling name"));
    }

    [Test]
    public void An_invalid_template_fails_instead_of_silently_renaming_the_product()
    {
        var rule = Rule();
        rule.StandaloneProductNameTemplate = "{root_variant_name";
        Assert.ThrowsAsync<InvalidOperationException>(() =>
            AkeneoStandaloneProductOverrides.ApplyAsync(Context("Seedling name", true), rule, Renderer));
    }

    private static AkeneoFamilySubModelRule Rule() => new()
    {
        VariantRelationshipOverrideMode = AkeneoVariantRelationshipMode.None,
        StandaloneProductNameTemplate = "{root_variant_name}"
    };

    private static AkeneoProductSyncContext Context(string? supplied, bool existing) => new()
    {
        Source = new AkeneoProductDefinition
        {
            Identifier = "SEEDLING-SKU", Family = "nursery", Enabled = true,
            Values = supplied == null ? JsonSerializer.SerializeToElement(new { }) : JsonSerializer.SerializeToElement(new
            {
                root_variant_name = new[] { new { data = supplied, locale = "en_US", scope = "ecommerce" } }
            })
        },
        SourceEntityType = AkeneoEntityType.Product,
        MappingEntityScope = AkeneoAttributeMappingEntityScope.StandaloneProduct,
        Request = new AkeneoProductImportRequest { Locale = "en_US", Channel = "ecommerce", Currency = "USD" },
        Result = new AkeneoProductImportResult(),
        Sku = "SEEDLING-SKU", ProductKey = "SEEDLING-SKU", MappingFamilyCode = "nursery",
        ExistingProduct = existing ? new Product { Id = 123, Name = "Old name", Sku = "SEEDLING-SKU" } : null,
        MappedValues = [new AkeneoResolvedMappedValue
        {
            Mapping = new AkeneoAttributeMapping { NopTargetTypeId = (int)NopTargetType.ProductField, NopTargetKey = "Name", AkeneoAttributeCode = "product_name" },
            HasValue = true,
            Value = new AkeneoResolvedProductValue { DisplayValue = "Normal mapped name" }
        }]
    };
}
