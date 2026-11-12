using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Moq;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Tax;
using Nop.Data;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

/// <summary>
/// A family mapping's new-product defaults are commerce settings Akeneo does not
/// own: set when the sync creates a product, never changed afterwards.
/// </summary>
[TestFixture]
public class AkeneoNewProductDefaultsTests
{
    private Mock<IProductService> _products = null!;
    private Mock<IAkeneoFamilyMappingService> _families = null!;
    private Product? _inserted;

    [SetUp]
    public void SetUp()
    {
        _products = new Mock<IProductService>();
        _families = new Mock<IAkeneoFamilyMappingService>();
        _inserted = null;

        _products.Setup(s => s.InsertProductAsync(It.IsAny<Product>()))
            .Callback<Product>(product => { product.Id = 500; _inserted = product; })
            .Returns(Task.CompletedTask);
        _families.Setup(s => s.GetEffectiveMappingAsync("trees", "trees_by_size")).ReturnsAsync(new AkeneoFamilyMapping
        {
            AkeneoFamilyCode = "trees",
            Enabled = true,
            NewProductTaxCategoryId = 3,
            NewProductIsShipEnabled = true,
            NewProductAllowCustomerReviews = false,
            NewProductWarehouseId = 2,
            NewProductTemplateId = 4
        });
    }

    private AkeneoProductCoreSynchronizer Synchronizer() =>
        new(_products.Object, Mock.Of<IRepository<Product>>(), _families.Object);

    [Test]
    public async Task A_created_product_gets_the_familys_defaults()
    {
        await Synchronizer().SynchronizeAsync(Context(existing: null));

        Assert.Multiple(() =>
        {
            Assert.That(_inserted, Is.Not.Null);
            Assert.That(_inserted!.TaxCategoryId, Is.EqualTo(3));
            Assert.That(_inserted.IsShipEnabled, Is.True);
            Assert.That(_inserted.AllowCustomerReviews, Is.False);
            Assert.That(_inserted.WarehouseId, Is.EqualTo(2));
            Assert.That(_inserted.ProductTemplateId, Is.EqualTo(4));
        });
    }

    [Test]
    public async Task An_existing_product_keeps_its_own_commerce_settings()
    {
        var existing = new Product { Id = 100, Sku = "SKU", Name = "Red Maple", TaxCategoryId = 9, WarehouseId = 0, IsShipEnabled = false, AllowCustomerReviews = true };

        await Synchronizer().SynchronizeAsync(Context(existing));

        Assert.Multiple(() =>
        {
            Assert.That(existing.TaxCategoryId, Is.EqualTo(9));
            Assert.That(existing.WarehouseId, Is.EqualTo(0));
            Assert.That(existing.IsShipEnabled, Is.False);
            Assert.That(existing.AllowCustomerReviews, Is.True);
        });
        _families.Verify(s => s.GetEffectiveMappingAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [TestCase("trees")]
    [TestCase("merchandise")]
    public async Task A_created_product_can_be_added_to_the_cart_like_one_made_on_the_admin_form(string family)
    {
        await Synchronizer().SynchronizeAsync(Context(existing: null, family: family));

        Assert.Multiple(() =>
        {
            Assert.That(_inserted!.OrderMinimumQuantity, Is.EqualTo(1));
            Assert.That(_inserted.OrderMaximumQuantity, Is.EqualTo(10000));
            Assert.That(_inserted.NotifyAdminForQuantityBelow, Is.EqualTo(1));

        });
    }

    [Test]
    public async Task An_existing_product_keeps_its_own_order_quantity_limits()
    {
        var existing = new Product { Id = 100, Sku = "SKU", Name = "Red Maple", OrderMinimumQuantity = 2, OrderMaximumQuantity = 5 };

        await Synchronizer().SynchronizeAsync(Context(existing));

        Assert.Multiple(() =>
        {
            Assert.That(existing.OrderMinimumQuantity, Is.EqualTo(2));
            Assert.That(existing.OrderMaximumQuantity, Is.EqualTo(5));
        });
    }

    [Test]
    public async Task A_family_without_a_mapping_keeps_the_nopcommerce_defaults()
    {
        var context = Context(existing: null, family: "merchandise");

        await Synchronizer().SynchronizeAsync(context);

        Assert.Multiple(() =>
        {
            Assert.That(_inserted!.TaxCategoryId, Is.EqualTo(0));
            Assert.That(_inserted.IsShipEnabled, Is.False);
            Assert.That(_inserted.AllowCustomerReviews, Is.True);
            Assert.That(_inserted.ProductTemplateId, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task Dry_run_shows_the_defaults_a_new_product_would_get()
    {
        var model = new AkeneoProductMappingPreviewModel();

        await Synchronizer().PlanAsync(Context(existing: null), model);

        Assert.That(model.Operations.Where(operation => operation.Area == "New product defaults").Select(operation => operation.Target),
            Is.EquivalentTo(new[] { "Allow customer reviews", "Shipping enabled", "Tax category", "Warehouse", "Product template" }));
        _products.Verify(s => s.InsertProductAsync(It.IsAny<Product>()), Times.Never);
    }

    [Test]
    public async Task Without_a_family_tax_category_a_created_product_gets_the_store_default_like_the_admin_form()
    {
        var taxSettings = new TaxSettings { DefaultTaxCategoryId = 6 };
        _families.Setup(s => s.GetEffectiveMappingAsync("trees", "trees_by_size"))
            .ReturnsAsync(new AkeneoFamilyMapping { AkeneoFamilyCode = "trees", Enabled = true, NewProductIsShipEnabled = true });

        await new AkeneoProductCoreSynchronizer(_products.Object, Mock.Of<IRepository<Product>>(), _families.Object, taxSettings)
            .SynchronizeAsync(Context(existing: null));
        var withMapping = _inserted!.TaxCategoryId;

        await new AkeneoProductCoreSynchronizer(_products.Object, Mock.Of<IRepository<Product>>(), _families.Object, taxSettings)
            .SynchronizeAsync(Context(existing: null, family: "merchandise"));

        Assert.Multiple(() =>
        {
            Assert.That(withMapping, Is.EqualTo(6));
            Assert.That(_inserted!.TaxCategoryId, Is.EqualTo(6), "a family without a mapping gets it too");
        });
    }

    [TestCase("trees")]
    [TestCase("merchandise")]
    public async Task Standalone_template_overrides_the_family_default_for_new_products_only(string family)
    {
        var context = Context(existing: null, family: family);
        context.StandaloneProductTemplateId = 7;
        await Synchronizer().SynchronizeAsync(context);
        Assert.That(_inserted!.ProductTemplateId, Is.EqualTo(7));

        var existing = new Product { Id = 100, Sku = "SKU", Name = "Red Maple", ProductTemplateId = 9 };
        var updateContext = Context(existing, family);
        updateContext.StandaloneProductTemplateId = 7;
        await Synchronizer().SynchronizeAsync(updateContext);
        Assert.That(existing.ProductTemplateId, Is.EqualTo(9));
    }

    [TestCase(null)]
    [TestCase(0)]
    public async Task An_unselected_standalone_template_keeps_the_family_default(int? templateId)
    {
        var context = Context(existing: null);
        context.StandaloneProductTemplateId = templateId;
        await Synchronizer().SynchronizeAsync(context);
        Assert.That(_inserted!.ProductTemplateId, Is.EqualTo(4));
    }

    [Test]
    public async Task Dry_run_and_apply_use_the_same_standalone_template()
    {
        var context = Context(existing: null);
        context.StandaloneProductTemplateId = 7;
        var preview = new AkeneoProductMappingPreviewModel();
        await Synchronizer().PlanAsync(context, preview);
        var template = preview.Operations.Single(operation => operation.Target == "Product template");
        Assert.That(System.Text.Json.JsonSerializer.Serialize(template), Does.Contain("Product template ID 7"));
        Assert.That(_inserted, Is.Null);
        await Synchronizer().SynchronizeAsync(context);
        Assert.That(_inserted!.ProductTemplateId, Is.EqualTo(7));
    }

    [Test]
    public async Task A_created_product_without_a_Published_mapping_starts_unpublished_even_when_enabled_in_Akeneo()
    {
        await Synchronizer().SynchronizeAsync(Context(existing: null));

        Assert.That(_inserted!.Published, Is.False);
    }

    [TestCase("true", true)]
    [TestCase("false", false)]
    public async Task A_created_product_is_published_from_its_Published_mapping(string value, bool expected)
    {
        var context = Context(existing: null, mapped: [PublishedMapping(value)]);

        await Synchronizer().SynchronizeAsync(context);

        Assert.That(_inserted!.Published, Is.EqualTo(expected));
    }

    [TestCase(AkeneoMissingValueBehavior.PreserveExisting)]
    [TestCase(AkeneoMissingValueBehavior.ClearExisting)]
    public async Task A_created_product_whose_Published_attribute_is_missing_starts_unpublished(AkeneoMissingValueBehavior behavior)
    {
        var context = Context(existing: null, mapped: [PublishedMapping(null)]);
        context.Request.ProductFieldMissingValueBehavior = behavior;

        await Synchronizer().SynchronizeAsync(context);

        Assert.That(_inserted!.Published, Is.False);
    }

    private static AkeneoResolvedMappedValue PublishedMapping(string? value) => new()
    {
        Mapping = new AkeneoAttributeMapping { NopTargetTypeId = (int)NopTargetType.ProductField, NopTargetKey = "Published", AkeneoAttributeCode = "published" },
        HasValue = value != null,
        Value = value == null ? null : new AkeneoResolvedProductValue { DisplayValue = value },
        EvaluationOutcome = AkeneoMappingEvaluationOutcome.Applied
    };

    private static AkeneoProductSyncContext Context(Product? existing, string family = "trees", AkeneoResolvedMappedValue[]? mapped = null) => new()
    {
        Source = new AkeneoProductDefinition
        {
            Identifier = "SKU", Uuid = "uuid", Family = family, Enabled = true,
            Values = JsonSerializer.SerializeToElement(new Dictionary<string, object>())
        },
        SourceEntityType = AkeneoEntityType.Product,
        Request = new AkeneoProductImportRequest(),
        Result = new AkeneoProductImportResult(),
        MappedValues = mapped ?? [],
        SourceCode = "SKU",
        Sku = "SKU",
        ProductKey = "SKU",
        MappingFamilyCode = family,
        MappingFamilyVariantCode = "trees_by_size",
        ExistingProduct = existing
    };
}
