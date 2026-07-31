using Apt.Nop.Plugin.Misc.AkeneoConnection.Extensions;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.TestData;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Helpers;

[TestFixture]
public class AkeneoConnectionExtensionsTests
{
    [Test]
    public void SplitCsv_trims_removes_blanks_and_deduplicates_case_insensitively()
    {
        var result = " Trees,shrubs, trees, ,FLOWERS ".SplitCsv();

        Assert.That(result, Is.EqualTo(new[] { "Trees", "shrubs", "FLOWERS" }));
    }

    [Test]
    public void SplitCsv_returns_empty_for_null_or_whitespace()
    {
        Assert.That(((string?)null).SplitCsv(), Is.Empty);
        Assert.That("   ".SplitCsv(), Is.Empty);
    }

    [Test]
    public void BuildCsv_trims_and_deduplicates_values()
    {
        var result = new[] { " Trees ", "trees", "Shrubs", "" }.BuildCsv();

        Assert.That(result, Is.EqualTo("Trees,Shrubs"));
    }

    [Test]
    public void BuildCsv_returns_null_when_no_meaningful_values_exist()
    {
        Assert.That(Array.Empty<string>().BuildCsv(), Is.Null);
        Assert.That(new[] { " ", "" }.BuildCsv(), Is.Null);
    }

    [Test]
    public void GetRootString_returns_only_string_properties()
    {
        var json = AkeneoTestData.Json("""{"name":"Maple","count":4}""");

        Assert.That(json.GetRootString("name"), Is.EqualTo("Maple"));
        Assert.That(json.GetRootString("count"), Is.Null);
        Assert.That(json.GetRootString("missing"), Is.Null);
    }
}
