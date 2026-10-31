using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Services.Catalog;
using Nop.Services.Customers;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

/// <summary>
/// Tier-price mappings accept a minimum quantity of 0 (ADF's member pricing reads
/// the role's quantity-0 tier), and a mapping whose quantity changed removes the
/// tier price it wrote in its old slot.
/// </summary>
[TestFixture]
public class AkeneoProductTierPriceSynchronizerTests
{
    private const int MemberRoleId = 9;

    private Mock<IProductService> _products = null!;
    private Mock<IAkeneoManagedRelationService> _relations = null!;
    private List<TierPrice> _tierPrices = null!;
    private List<AkeneoManagedRelation> _owned = null!;

    [SetUp]
    public void SetUp()
    {
        _tierPrices = new List<TierPrice>();
        _owned = new List<AkeneoManagedRelation>();
        _products = new Mock<IProductService>();
        _products.Setup(s => s.GetTierPricesByProductAsync(2127)).ReturnsAsync(() => _tierPrices);
        _products.Setup(s => s.InsertTierPriceAsync(It.IsAny<TierPrice>())).Callback<TierPrice>(t => t.Id = 900).Returns(Task.CompletedTask);
        _relations = new Mock<IAkeneoManagedRelationService>();
        _relations.Setup(s => s.GetByProductAsync(7, 2127, AkeneoManagedRelationType.TierPrice, null)).ReturnsAsync(() => _owned);
        _relations.Setup(s => s.UpsertAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<AkeneoManagedRelationType>(),
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(new AkeneoManagedRelation());
    }

    private AkeneoProductTierPriceSynchronizer Synchronizer()
    {
        var customers = new Mock<ICustomerService>();
        customers.Setup(s => s.GetCustomerRoleByIdAsync(MemberRoleId))
            .ReturnsAsync(new CustomerRole { Id = MemberRoleId, Name = "Member", SystemName = "Adf.Member" });
        return new AkeneoProductTierPriceSynchronizer(_products.Object, customers.Object, _relations.Object);
    }

    private static AkeneoProductSyncContext Context(string quantity, string price = "39.99")
    {
        var product = new Product { Id = 2127 };
        return new AkeneoProductSyncContext
        {
            Request = new AkeneoProductImportRequest { SyncProfileId = 7, SyncRunRecordId = 20 },
            Result = new AkeneoProductImportResult(),
            ExistingProduct = product,
            Product = product,
            MappedValues =
            [
                new AkeneoResolvedMappedValue
                {
                    Mapping = new AkeneoAttributeMapping
                    {
                        AkeneoAttributeCode = "member_price",
                        NopTargetTypeId = (int)NopTargetType.TierPrice,
                        NopTargetEntityId = MemberRoleId,
                        NopTargetKey = quantity
                    },
                    HasValue = true,
                    Value = new AkeneoResolvedProductValue
                    {
                        AttributeCode = "member_price",
                        DisplayValue = price,
                        RawData = JsonSerializer.SerializeToElement(price)
                    }
                }
            ]
        };
    }

    [TestCase("0", 0)]
    [TestCase("5", 5)]
    [TestCase("", 1)]
    [TestCase("-1", 1)]
    [TestCase("abc", 1)]
    public void The_minimum_quantity_accepts_0_and_defaults_to_1(string key, int quantity) =>
        Assert.That(AkeneoProductTierPriceSynchronizer.GetMinimumQuantity(new AkeneoAttributeMapping { NopTargetKey = key }), Is.EqualTo(quantity));

    [Test]
    public async Task A_quantity_0_mapping_writes_a_quantity_0_tier_price()
    {
        await Synchronizer().SynchronizeAsync(Context("0"));

        _products.Verify(s => s.InsertTierPriceAsync(It.Is<TierPrice>(t =>
            t.Quantity == 0 && t.CustomerRoleId == MemberRoleId && t.Price == 39.99m)), Times.Once);
    }

    [Test]
    public async Task Moving_to_quantity_0_removes_only_the_tier_price_this_mapping_wrote_for_1()
    {
        var ownStale = new TierPrice { Id = 2519, ProductId = 2127, CustomerRoleId = MemberRoleId, Quantity = 1, Price = 39.99m };
        var otherAttribute = new TierPrice { Id = 600, ProductId = 2127, CustomerRoleId = MemberRoleId, Quantity = 10, Price = 35m };
        var handMade = new TierPrice { Id = 601, ProductId = 2127, CustomerRoleId = MemberRoleId, Quantity = 25, Price = 30m };
        _tierPrices.AddRange([ownStale, otherAttribute, handMade]);
        _owned.Add(new AkeneoManagedRelation { NopRelationEntityId = 2519, AkeneoAttributeCode = "member_price" });
        _owned.Add(new AkeneoManagedRelation { NopRelationEntityId = 600, AkeneoAttributeCode = "member_bulk_price" });

        await Synchronizer().SynchronizeAsync(Context("0"));

        _products.Verify(s => s.DeleteTierPriceAsync(ownStale), Times.Once);
        _products.Verify(s => s.DeleteTierPriceAsync(otherAttribute), Times.Never);
        _products.Verify(s => s.DeleteTierPriceAsync(handMade), Times.Never);
        _products.Verify(s => s.InsertTierPriceAsync(It.Is<TierPrice>(t => t.Quantity == 0)), Times.Once);
    }
}
