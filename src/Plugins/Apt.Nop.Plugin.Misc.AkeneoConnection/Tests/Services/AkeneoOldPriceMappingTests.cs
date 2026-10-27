using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Moq;
using Nop.Core.Domain.Catalog;
using Nop.Data;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

/// <summary>
/// OldPrice is the list price nopCommerce shows struck through next to Price.
/// </summary>
[TestFixture]
public class AkeneoOldPriceMappingTests
{
    [Test]
    public void OldPrice_is_offered_as_a_product_field()
    {
        var options = new AkeneoTargetTypeResolver().GetTargetKeyOptions(NopTargetType.ProductField);

        Assert.That(options.Select(option => option.Code), Does.Contain("OldPrice"));
    }

    [Test]
    public async Task A_mapped_list_price_is_written_to_OldPrice()
    {
        var product = new Product { Id = 100, Price = 24.99m, OldPrice = 0m };

        await Synchronize(product, "34.99");

        Assert.Multiple(() =>
        {
            Assert.That(product.OldPrice, Is.EqualTo(34.99m));
            Assert.That(product.Price, Is.EqualTo(24.99m));
        });
    }

    [Test]
    public async Task An_empty_list_price_clears_OldPrice_when_the_profile_clears_missing_values()
    {
        var product = new Product { Id = 100, Price = 24.99m, OldPrice = 34.99m };

        await Synchronize(product, null, AkeneoMissingValueBehavior.ClearExisting);

        Assert.That(product.OldPrice, Is.EqualTo(0m));
    }

    [Test]
    public async Task An_empty_list_price_is_kept_when_the_profile_preserves_missing_values()
    {
        var product = new Product { Id = 100, Price = 24.99m, OldPrice = 34.99m };

        await Synchronize(product, null, AkeneoMissingValueBehavior.PreserveExisting);

        Assert.That(product.OldPrice, Is.EqualTo(34.99m));
    }

    private static Task Synchronize(
        Product product,
        string? value,
        AkeneoMissingValueBehavior missing = AkeneoMissingValueBehavior.PreserveExisting)
    {
        var context = new AkeneoProductSyncContext
        {
            Source = new AkeneoProductDefinition
            {
                Identifier = "SKU", Uuid = "uuid", Enabled = true,
                Values = JsonSerializer.SerializeToElement(new Dictionary<string, object>())
            },
            Request = new AkeneoProductImportRequest { ProductFieldMissingValueBehavior = missing },
            Result = new AkeneoProductImportResult(),
            ExistingProduct = product,
            MappedValues =
            [
                new AkeneoResolvedMappedValue
                {
                    Mapping = new AkeneoAttributeMapping
                    {
                        AkeneoAttributeCode = "msrp",
                        NopTargetTypeId = (int)NopTargetType.ProductField,
                        NopTargetKey = "OldPrice"
                    },
                    HasValue = value != null,
                    Value = value == null ? null : new AkeneoResolvedProductValue { AttributeCode = "msrp", DisplayValue = value }
                }
            ]
        };

        return new AkeneoProductCoreSynchronizer(Mock.Of<IProductService>(), Mock.Of<IRepository<Product>>())
            .SynchronizeAsync(context);
    }
}
