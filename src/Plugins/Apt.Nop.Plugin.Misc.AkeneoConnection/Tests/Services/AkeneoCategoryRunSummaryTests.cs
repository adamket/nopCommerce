using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

[TestFixture]
public class AkeneoCategoryRunSummaryTests
{
    [TestCase("success")]
    [TestCase("failure")]
    [TestCase("canceled")]
    public async Task Run_persists_one_deduplicated_summary_even_when_interrupted_and_does_not_leak_into_next_run(string outcome)
    {
        var request = new AkeneoProductBatchImportRequest { SyncRunRecordId = 23, RunMode = AkeneoRunMode.Resync };
        var api = new Mock<IAkeneoApiClient>();
        var logs = new List<AkeneoSyncItemLog>();
        var logService = new Mock<IAkeneoSyncItemLogService>();
        logService.Setup(x => x.InsertAkeneoSyncItemLogAsync(It.IsAny<AkeneoSyncItemLog>()))
            .Callback<AkeneoSyncItemLog>(logs.Add).Returns(Task.CompletedTask);
        api.Setup(x => x.GetProductsPageAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                request.CategoryMappingWarnings.Add("shade");
                request.CategoryMappingWarnings.Add("shade");
                request.CategoryMappingWarnings.Add("fast_growing_deciduous_trees");
                request.AssociationWarnings.Replace("Cross-sell products", AkeneoEntityType.Product, "A", new[] { "Missing target" });
                request.AssociationWarnings.Replace("Related products", AkeneoEntityType.Product, "A", new[] { "Resolved on retry" });
                request.AssociationWarnings.Replace("Related products", AkeneoEntityType.Product, "A", Array.Empty<string>());
                var product = new AkeneoProductImportResult { NopProductId = 1052 };
                product.MissingTokenGroups.Add(new[] { "plant_info.min", "plant_info.max" });
                request.MissingTokenWarnings.Collect(product);
                product.NopProductId = 1051;
                product.MissingTokenGroups.Clear();
                product.MissingTokenGroups.Add(new[] { "plant_info.max", "plant_info.min" });
                request.MissingTokenWarnings.Collect(product);
                request.MissingTokenWarnings.Collect(product);
                if (outcome == "failure")
                    throw new InvalidOperationException("Source failed");
                if (outcome == "canceled")
                    throw new OperationCanceledException();
                return Task.FromResult(new AkeneoProductPageResult());
            });
        var service = new AkeneoProductBatchSyncService(
            api.Object, Mock.Of<IAkeneoProductValueResolver>(), Mock.Of<IAkeneoNopEntityMappingService>(),
            logService.Object, Mock.Of<IProductService>(), Mock.Of<IAkeneoFamilyMappingService>(),
            new AkeneoProductModelHierarchyResolver(), Mock.Of<IAkeneoVariantRelationshipService>(),
            Mock.Of<IAkeneoLeafRepresentationClassifier>(), Mock.Of<IAkeneoProductSyncService>(),
            Mock.Of<IAkeneoProductSyncStateService>(), Mock.Of<IAkeneoVariantRepresentationCleanupService>(),
            Mock.Of<IAkeneoCatalogReconciliationService>(), Mock.Of<IAkeneoSyncLeaseService>(),
            Mock.Of<IAkeneoProductModelDeltaFanOutService>(), Mock.Of<IAkeneoDryRunChangeAnalyzer>(),
            Mock.Of<IAkeneoAssetMappingService>(), Mock.Of<IAkeneoAssetResolver>(), Mock.Of<IAkeneoProfileRuleEvaluator>());

        var result = await service.SyncProductsAsync(request);

        Assert.That(logs, Has.Count.EqualTo(1));
        Assert.That(logs[0].AkeneoIdentifier, Is.EqualTo("Run complete"));
        Assert.That(logs[0].ActionTypeId, Is.EqualTo((int)SyncItemActionType.RunComplete));
        Assert.That(logs[0].SyncRunRecordId, Is.EqualTo(23));
        Assert.That(logs[0].Message, Does.Contain("Unresolved category mappings (2): fast_growing_deciduous_trees, shade."));
        Assert.That(logs[0].Message, Does.Contain("Cross-sell products:").And.Contain("Missing target (Sources: A)").And.Not.Contain("Resolved on retry"));
        Assert.That(logs[0].Message, Does.Contain("Products missing: {plant_info.min}, {plant_info.max}" + Environment.NewLine + "1051, 1052"));
        Assert.That(result.WarningCount, Is.EqualTo(3));
        Assert.That(result.SyncStatus, Is.EqualTo(outcome == "canceled" ? SyncStatus.Cancelled
            : outcome == "failure" ? SyncStatus.CompletedWithErrors : SyncStatus.CompletedWithWarnings));
        Assert.That(request.CategoryMappingWarnings, Is.Null);

        api.Setup(x => x.GetProductsPageAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AkeneoProductPageResult());
        request.SyncRunRecordId = 24;
        var next = await service.SyncProductsAsync(request);
        Assert.That(logs, Has.Count.EqualTo(2));
        Assert.That(logs[1].SyncRunRecordId, Is.EqualTo(24));
        Assert.That(logs[1].Message, Does.Not.Contain("category mappings"));
        Assert.That(next.WarningCount, Is.Zero);
    }
}
