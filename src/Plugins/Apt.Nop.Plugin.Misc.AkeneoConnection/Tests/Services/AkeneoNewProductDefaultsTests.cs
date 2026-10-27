using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
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
            Assert.That(_inserted.WarehouseId, Is.EqualTo(2));
            Assert.That(_inserted.ProductTemplateId, Is.EqualTo(4));
        });
    }

    [Test]
    public async Task An_existing_product_keeps_its_own_commerce_settings()
    {
        var existing = new Product { Id = 100, Sku = "SKU", Name = "Red Maple", TaxCategoryId = 9, WarehouseId = 0, IsShipEnabled = false };

        await Synchronizer().SynchronizeAsync(Context(existing));

        Assert.Multiple(() =>
        {
            Assert.That(existing.TaxCategoryId, Is.EqualTo(9));
            Assert.That(existing.WarehouseId, Is.EqualTo(0));
            Assert.That(existing.IsShipEnabled, Is.False);
        });
        _families.Verify(s => s.GetEffectiveMappingAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
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
            Assert.That(_inserted.ProductTemplateId, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task Dry_run_shows_the_defaults_a_new_product_would_get()
    {
        var model = new AkeneoProductMappingPreviewModel();

        await Synchronizer().PlanAsync(Context(existing: null), model);

        Assert.That(model.Operations.Where(operation => operation.Area == "New product defaults").Select(operation => operation.Target),
            Is.EquivalentTo(new[] { "Shipping enabled", "Tax category", "Warehouse", "Product template" }));
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

    private static AkeneoProductSyncContext Context(Product? existing, string family = "trees") => new()
    {
        Source = new AkeneoProductDefinition
        {
            Identifier = "SKU", Uuid = "uuid", Family = family, Enabled = true,
            Values = JsonSerializer.SerializeToElement(new Dictionary<string, object>())
        },
        SourceEntityType = AkeneoEntityType.Product,
        Request = new AkeneoProductImportRequest(),
        Result = new AkeneoProductImportResult(),
        MappedValues = [],
        SourceCode = "SKU",
        Sku = "SKU",
        ProductKey = "SKU",
        MappingFamilyCode = family,
        MappingFamilyVariantCode = "trees_by_size",
        ExistingProduct = existing
    };
}
