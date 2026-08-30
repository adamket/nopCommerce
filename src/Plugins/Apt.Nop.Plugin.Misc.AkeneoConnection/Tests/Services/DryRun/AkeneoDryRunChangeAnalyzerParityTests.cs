using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services.DryRun;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using NUnit.Framework;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

[TestFixture]
public class AkeneoDryRunChangeAnalyzerParityTests
{
    [TestCase(typeof(AkeneoProductCoreSynchronizer))]
    [TestCase(typeof(AkeneoProductSeoSynchronizer))]
    [TestCase(typeof(AkeneoProductCategorySynchronizer))]
    [TestCase(typeof(AkeneoProductSpecificationSynchronizer))]
    [TestCase(typeof(AkeneoProductAttributeSynchronizer))]
    [TestCase(typeof(AkeneoProductAssetSynchronizer))]
    [TestCase(typeof(AkeneoProductCustomPropertySynchronizer))]
    public void Every_destination_writer_exposes_the_same_read_only_plan_contract(
        Type synchronizerType)
    {
        Assert.That(
            typeof(IAkeneoSectionDryRunPlanProvider).IsAssignableFrom(synchronizerType),
            Is.True,
            $"{synchronizerType.Name} writes destination state but does not expose " +
            $"{nameof(IAkeneoSectionDryRunPlanProvider)}.");
    }

    [Test]
    public void Analyzer_rejects_a_write_synchronizer_without_a_plan_provider()
    {
        var synchronizers = new IAkeneoProductSectionSynchronizer[]
        {
            new WriteOnlySynchronizer()
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            _ = new AkeneoDryRunChangeAnalyzer(
                synchronizers,
                Array.Empty<IAkeneoDryRunSectionPlanner>()));

        Assert.That(exception!.Message, Does.Contain(nameof(IAkeneoSectionDryRunPlanProvider)));
    }

    [Test]
    public async Task Analyzer_invokes_the_plan_owned_by_the_write_synchronizer()
    {
        var synchronizer = new PlannedSynchronizer();
        var analyzer = new AkeneoDryRunChangeAnalyzer(
            new IAkeneoProductSectionSynchronizer[] { synchronizer },
            Array.Empty<IAkeneoDryRunSectionPlanner>());
        var context = new AkeneoProductSyncContext();
        var model = new AkeneoProductMappingPreviewModel();

        await analyzer.AnalyzeAsync(context, model);

        Assert.Multiple(() =>
        {
            Assert.That(synchronizer.PlanCallCount, Is.EqualTo(1));
            Assert.That(model.Operations, Has.Count.EqualTo(1));
            Assert.That(model.Operations[0].Area, Is.EqualTo("Parity test"));
            Assert.That(model.Operations[0].ProposedValue, Is.EqualTo("desired"));
        });
    }

    private sealed class WriteOnlySynchronizer : IAkeneoProductSectionSynchronizer
    {
        public int Order => 100;

        public Task SynchronizeAsync(
            AkeneoProductSyncContext context,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class PlannedSynchronizer
        : IAkeneoProductSectionSynchronizer,
          IAkeneoSectionDryRunPlanProvider
    {
        public int Order => 100;

        public int PlanCallCount { get; private set; }

        public Task SynchronizeAsync(
            AkeneoProductSyncContext context,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task PlanAsync(
            AkeneoProductSyncContext context,
            AkeneoProductMappingPreviewModel model,
            CancellationToken cancellationToken = default)
        {
            PlanCallCount++;
            model.Operations.Add(new AkeneoDryRunOperationPreviewModel
            {
                DisplayOrder = 100,
                Area = "Parity test",
                Target = "Target",
                AkeneoSource = "source",
                CurrentValue = "current",
                ProposedValue = "desired",
                ChangeType = AkeneoDryRunOperationType.Update
            });

            return Task.CompletedTask;
        }
    }
}
