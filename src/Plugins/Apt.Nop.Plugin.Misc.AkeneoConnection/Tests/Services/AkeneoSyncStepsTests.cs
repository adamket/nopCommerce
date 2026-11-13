using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

[TestFixture]
public class AkeneoSyncStepsTests
{
    private readonly List<string> _reports = [];

    private AkeneoSyncSteps Steps() => new((step, unit, done, total) => _reports.Add($"{step}: {done} of {total} {unit}"));

    [Test]
    public void Each_report_reaches_the_sync_page_with_its_step_and_unit()
    {
        var steps = Steps();
        steps.Report(AkeneoSyncSteps.Products, "products", 0, 2);
        steps.Report(AkeneoSyncSteps.Products, "products", 2, 2);
        steps.Report("Retrying cross-sell products links", "products", 1, 3);

        Assert.That(_reports, Is.EqualTo(new[]
        {
            "Processing products: 0 of 2 products",
            "Processing products: 2 of 2 products",
            "Retrying cross-sell products links: 1 of 3 products"
        }));
    }

    [Test]
    public void A_step_without_work_is_neither_shown_nor_timed()
    {
        var steps = Steps();
        steps.Report("Applying the missing-product rule", "products no longer in Akeneo", 0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(_reports, Is.Empty);
            Assert.That(steps.BuildSummary(), Is.Null);
        });
    }

    [Test]
    public void The_summary_times_every_step_in_order_with_its_size()
    {
        var steps = Steps();
        steps.Report(AkeneoSyncSteps.Products, "products", 5, 5);
        steps.Report("Checking associated options against Akeneo", "grouped products", 0, 3);
        steps.Report("Checking associated options against Akeneo", "grouped products", 3, 3);

        Assert.That(steps.BuildSummary(), Does.Match(
            @"^Step timings: Processing products \d+\.\ds \(5 products\); Checking associated options against Akeneo \d+\.\ds \(3 grouped products\)\.$"));
    }

    [TestCase(0.24, "0.2s")]
    [TestCase(59, "59.0s")]
    [TestCase(61, "1m 1s")]
    [TestCase(1402, "23m 22s")]
    public void Durations_read_as_minutes_and_seconds(double seconds, string expected) =>
        Assert.That(AkeneoSyncSteps.Format(TimeSpan.FromSeconds(seconds)), Is.EqualTo(expected));
}
