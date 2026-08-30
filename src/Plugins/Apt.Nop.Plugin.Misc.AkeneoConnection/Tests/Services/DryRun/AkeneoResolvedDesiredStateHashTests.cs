using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using NUnit.Framework;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

[TestFixture]
public class AkeneoResolvedDesiredStateHashTests
{
    [TestCase("Product fields")]
    [TestCase("SEO")]
    [TestCase("Categories")]
    [TestCase("Specifications")]
    [TestCase("Product attributes")]
    [TestCase("Assets")]
    [TestCase("Custom properties")]
    public async Task Changing_only_one_planned_section_changes_the_hash(string area)
    {
        var analyzer = new SingleOperationAnalyzer(area, "before");
        var service = CreateBatchService(analyzer);
        var request = CreateRequest();
        var context = CreateContext(request);

        var before = await service.BuildResolvedContextHashAsync(
            context,
            request,
            new { Path = "standalone" },
            CancellationToken.None);

        analyzer.ProposedValue = "after";

        var after = await service.BuildResolvedContextHashAsync(
            context,
            request,
            new { Path = "standalone" },
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.Not.Null.And.Not.Empty);
            Assert.That(after, Is.Not.Null.And.Not.Empty);
            Assert.That(after, Is.Not.EqualTo(before));
        });
    }

    [Test]
    public async Task Relevant_write_policy_change_changes_the_hash()
    {
        var analyzer = new SingleOperationAnalyzer("Product fields", "same");
        var service = CreateBatchService(analyzer);
        var request = CreateRequest();
        var context = CreateContext(request);

        var before = await service.BuildResolvedContextHashAsync(
            context,
            request,
            new { Path = "standalone" },
            CancellationToken.None);

        request.ProductFieldMissingValueBehavior =
            AkeneoMissingValueBehavior.ClearExisting;

        var after = await service.BuildResolvedContextHashAsync(
            context,
            request,
            new { Path = "standalone" },
            CancellationToken.None);

        Assert.That(after, Is.Not.EqualTo(before));
    }

    [Test]
    public async Task Unmapped_source_payload_change_does_not_change_the_hash()
    {
        var analyzer = new SingleOperationAnalyzer("Product fields", "same");
        var service = CreateBatchService(analyzer);
        var request = CreateRequest();
        var context = CreateContext(request);

        var before = await service.BuildResolvedContextHashAsync(
            context,
            request,
            new { Path = "standalone" },
            CancellationToken.None);

        // Raw Akeneo values are intentionally not hashed wholesale. Only
        // resolved mapped values, section plans, categories/assets, and other
        // destination-affecting inputs belong in the desired-state hash.
        context.Source.Values = JsonSerializer.SerializeToElement(new
        {
            totally_unmapped_attribute = "changed"
        });

        var after = await service.BuildResolvedContextHashAsync(
            context,
            request,
            new { Path = "standalone" },
            CancellationToken.None);

        Assert.That(after, Is.EqualTo(before));
    }

    [Test]
    public async Task Analyzer_uncertainty_returns_null_instead_of_a_skippable_hash()
    {
        var analyzer = new ErrorAnalyzer();
        var service = CreateBatchService(analyzer);
        var request = CreateRequest();
        var context = CreateContext(request);

        var hash = await service.BuildResolvedContextHashAsync(
            context,
            request,
            new { Path = "standalone" },
            CancellationToken.None);

        Assert.That(hash, Is.Null);
    }

    private static AkeneoProductBatchSyncService CreateBatchService(
        IAkeneoDryRunChangeAnalyzer analyzer)
    {
        // This test exercises only BuildResolvedContextHashAsync. Asset sync is
        // disabled and the source has no categories, so unrelated collaborators
        // are intentionally null. If nullable warnings are errors in your test
        // project, keep the null-forgiving operators as shown.
        return new AkeneoProductBatchSyncService(
            null!, // IAkeneoApiClient
            null!, // IAkeneoProductValueResolver
            null!, // IAkeneoNopEntityMappingService
            null!, // IAkeneoSyncItemLogService
            null!, // IProductService
            null!, // IAkeneoFamilyMappingService
            null!, // IAkeneoProductModelHierarchyResolver
            null!, // IAkeneoVariantRelationshipService
            null!, // IAkeneoLeafRepresentationClassifier
            null!, // IAkeneoProductSyncService
            null!, // IAkeneoProductSyncStateService
            null!, // IAkeneoVariantRepresentationCleanupService
            null!, // IAkeneoCatalogReconciliationService
            null!, // IAkeneoSyncLeaseService
            null!, // IAkeneoProductModelDeltaFanOutService
            analyzer,
            null!, // IAkeneoAssetMappingService
            null!  // IAkeneoAssetResolver
        );
    }

    private static AkeneoProductImportRequest CreateRequest() => new()
    {
        SyncRunRecordId = 1,
        SyncProfileId = 1,
        RunMode = AkeneoRunMode.Delta,
        Locale = "en_US",
        Channel = "ecommerce",
        Currency = "USD",
        AssetSyncMode = AkeneoCollectionSyncMode.Disabled
    };

    private static AkeneoProductSyncContext CreateContext(
        AkeneoProductImportRequest request)
    {
        var source = new AkeneoProductDefinition
        {
            Uuid = "uuid-1",
            Identifier = "SKU-1",
            Family = "family",
            Enabled = true,
            Categories = new List<string>(),
            Values = JsonSerializer.SerializeToElement(new { })
        };

        return new AkeneoProductSyncContext
        {
            Source = source,
            SourceEntityType = AkeneoEntityType.Product,
            Request = request,
            Result = new AkeneoProductImportResult(),
            SourceCode = source.Identifier,
            SourceUuid = source.Uuid,
            ProductKey = source.Uuid,
            Sku = source.Identifier,
            MappingFamilyCode = source.Family,
            MappedValues = Array.Empty<AkeneoResolvedMappedValue>()
        };
    }

    private sealed class SingleOperationAnalyzer : IAkeneoDryRunChangeAnalyzer
    {
        private readonly string _area;

        public SingleOperationAnalyzer(string area, string proposedValue)
        {
            _area = area;
            ProposedValue = proposedValue;
        }

        public string ProposedValue { get; set; }

        public Task AnalyzeAsync(
            AkeneoProductSyncContext context,
            AkeneoProductMappingPreviewModel model,
            CancellationToken cancellationToken = default)
        {
            model.Operations.Add(new AkeneoDryRunOperationPreviewModel
            {
                DisplayOrder = 100,
                Area = _area,
                Target = "Target",
                AkeneoSource = "source",
                CurrentValue = "current",
                ProposedValue = ProposedValue,
                ChangeType = AkeneoDryRunOperationType.Update
            });

            return Task.CompletedTask;
        }
    }

    private sealed class ErrorAnalyzer : IAkeneoDryRunChangeAnalyzer
    {
        public Task AnalyzeAsync(
            AkeneoProductSyncContext context,
            AkeneoProductMappingPreviewModel model,
            CancellationToken cancellationToken = default)
        {
            model.Errors.Add("Simulated analyzer uncertainty.");
            return Task.CompletedTask;
        }
    }
}
