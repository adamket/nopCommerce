using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Data;
using Nop.Services.Catalog;
using Nop.Services.Customers;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

/// <summary>Covers the per-item reads and writes the sync no longer repeats.</summary>
[TestFixture]
public class AkeneoSyncWriteReductionTests
{
    [Test]
    public async Task Unchanged_managed_relation_is_not_rewritten()
    {
        var existing = new AkeneoManagedRelation
        {
            Id = 1, SyncProfileId = 7, NopProductId = 10, RelationTypeId = (int)AkeneoManagedRelationType.TierPrice,
            NopRelationEntityId = 50, AkeneoAttributeCode = "price", LastSeenRunRecordId = 3
        };
        var repository = new Mock<IRepository<AkeneoManagedRelation>>();
        repository.Setup(r => r.Table).Returns(new[] { existing }.AsQueryable());
        var service = new AkeneoManagedRelationService(repository.Object);

        await service.UpsertAsync(7, 20, 10, AkeneoManagedRelationType.TierPrice, 50, " price ");

        repository.Verify(r => r.UpdateAsync(It.IsAny<AkeneoManagedRelation>(), It.IsAny<bool>()), Times.Never);
        Assert.That(existing.LastSeenRunRecordId, Is.EqualTo(3));

        await service.UpsertAsync(7, 20, 10, AkeneoManagedRelationType.TierPrice, 50, "other_price");

        repository.Verify(r => r.UpdateAsync(existing, It.IsAny<bool>()), Times.Once);
        Assert.That(existing.LastSeenRunRecordId, Is.EqualTo(20));
    }

    [TestCase(true, 0)]
    [TestCase(false, 1)]
    public async Task Unchanged_tier_price_is_claimed_only_when_not_already_owned(bool alreadyOwned, int expectedClaims)
    {
        var tierPrice = new TierPrice { Id = 50, ProductId = 10, CustomerRoleId = 3, Quantity = 1, Price = 9.5m };
        var products = new Mock<IProductService>();
        products.Setup(p => p.GetTierPricesByProductAsync(10)).ReturnsAsync(new List<TierPrice> { tierPrice });
        var customers = new Mock<ICustomerService>();
        customers.Setup(c => c.GetCustomerRoleByIdAsync(3)).ReturnsAsync(new CustomerRole { Id = 3, Name = "Members" });
        var relations = new Mock<IAkeneoManagedRelationService>();
        relations
            .Setup(r => r.GetByProductAsync(7, 10, AkeneoManagedRelationType.TierPrice, null))
            .ReturnsAsync(alreadyOwned
                ? new List<AkeneoManagedRelation> { new() { NopRelationEntityId = 50 } }
                : new List<AkeneoManagedRelation>());
        var synchronizer = new AkeneoProductTierPriceSynchronizer(products.Object, customers.Object, relations.Object);

        await synchronizer.SynchronizeAsync(TierPriceContext("9.5"));

        relations.Verify(r => r.UpsertAsync(
                7, 20, 10, AkeneoManagedRelationType.TierPrice, 50, "membership_price", null),
            Times.Exactly(expectedClaims));
        relations.Verify(r => r.GetByRelationEntityAsync(
            It.IsAny<int>(), It.IsAny<AkeneoManagedRelationType>(), It.IsAny<int>()), Times.Never);
    }

    [TestCase("uuid-1", "SKU-1", true)]
    [TestCase("UUID-1", "other", true)]
    [TestCase("uuid-2", "SKU-1", false)]
    [TestCase(null, "sku-1", true)]
    [TestCase(null, "SKU-2", false)]
    public void Known_state_is_reused_only_for_its_own_source(string uuid, string code, bool expected)
    {
        var state = new AkeneoProductSyncState
        {
            SyncProfileId = 7, AkeneoEntityTypeId = (int)AkeneoEntityType.Product,
            AkeneoUuid = "uuid-1", AkeneoCode = "SKU-1"
        };

        Assert.Multiple(() =>
        {
            Assert.That(AkeneoProductSyncStateService.IsStateFor(state, 7, AkeneoEntityType.Product, code, uuid),
                Is.EqualTo(expected));
            Assert.That(AkeneoProductSyncStateService.IsStateFor(state, 8, AkeneoEntityType.Product, code, uuid),
                Is.False, "A state from another profile is never reused.");
            Assert.That(AkeneoProductSyncStateService.IsStateFor(null, 7, AkeneoEntityType.Product, code, uuid),
                Is.False);
        });
    }

    [Test]
    public void Item_warnings_are_distinct_ordered_and_capped()
    {
        var result = new AkeneoProductBatchImportResult();

        result.AddItemWarnings(["b", "a", "b", " "]);
        result.AddItemWarnings(Enumerable.Range(0, 200).Select(index => $"w{index}"));

        Assert.Multiple(() =>
        {
            Assert.That(result.ItemWarnings.Take(3), Is.EqualTo(new[] { "b", "a", "w0" }));
            Assert.That(result.ItemWarnings, Has.Count.EqualTo(AkeneoProductBatchImportResult.MaxItemWarnings));
            Assert.That(result.ItemWarnings, Is.Unique);
        });
    }

    private static AkeneoProductSyncContext TierPriceContext(string price)
    {
        var product = new Product { Id = 10 };

        return new AkeneoProductSyncContext
        {
            Source = new AkeneoProductDefinition
            {
                Uuid = "uuid-1",
                Identifier = "SKU-1",
                Family = "family",
                Enabled = true,
                Values = JsonSerializer.SerializeToElement(new Dictionary<string, object>())
            },
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
                        AkeneoAttributeCode = "membership_price",
                        NopTargetTypeId = (int)NopTargetType.TierPrice,
                        NopTargetEntityId = 3,
                        NopTargetKey = "1"
                    },
                    HasValue = true,
                    Value = new AkeneoResolvedProductValue
                    {
                        AttributeCode = "membership_price",
                        DisplayValue = price,
                        RawData = JsonSerializer.SerializeToElement(price)
                    },
                    ResolvedSourceDisplayName = "membership_price"
                }
            ]
        };
    }
}
