using Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.TestData;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Types;

[TestFixture]
public class AkeneoSyncValueHelperTests
{
    [Test]
    public void GetRawItems_converts_scalars_and_deduplicates_case_insensitively()
    {
        var mapped = Mapped("[\"bare_root\",\" BARE_ROOT \",5,true]");

        Assert.That(
            AkeneoSyncValueHelper.GetRawItems(mapped),
            Is.EqualTo(new[] { "bare_root", "5", "true" }));
    }

    [Test]
    public void GetOptionItems_pairs_codes_and_labels_before_deduplication()
    {
        var mapped = Mapped(
            "[\"bare_root\",\"potted\",\"BARE_ROOT\"]",
            new[] { "Bare Root", "Potted", "Duplicate label should be ignored" });

        var items = AkeneoSyncValueHelper.GetOptionItems(mapped);

        Assert.Multiple(() =>
        {
            Assert.That(items.Select(item => item.AkeneoOptionCode),
                Is.EqualTo(new[] { "bare_root", "potted" }));
            Assert.That(items.Select(item => item.DisplayName),
                Is.EqualTo(new[] { "Bare Root", "Potted" }));
        });
    }

    [Test]
    public void GetOptionItems_uses_code_when_label_is_missing()
    {
        var mapped = Mapped("[\"bare_root\",\"potted\"]", new[] { "Bare Root" });

        var items = AkeneoSyncValueHelper.GetOptionItems(mapped);

        Assert.That(items.Select(item => item.DisplayName), Is.EqualTo(new[] { "Bare Root", "potted" }));
    }

    [Test]
    public void GetOptionItems_can_create_label_only_items()
    {
        var mapped = new AkeneoResolvedMappedValue
        {
            HasValue = true,
            Value = new AkeneoResolvedProductValue
            {
                DisplayValues = new[] { "Bare Root" },
                DisplayValue = "Bare Root"
            }
        };

        var item = AkeneoSyncValueHelper.GetOptionItems(mapped).Single();

        Assert.Multiple(() =>
        {
            Assert.That(item.AkeneoOptionCode, Is.Null);
            Assert.That(item.DisplayName, Is.EqualTo("Bare Root"));
        });
    }

    private static AkeneoResolvedMappedValue Mapped(
        string rawJson,
        IReadOnlyList<string>? displayValues = null) => new()
        {
            HasValue = true,
            Value = new AkeneoResolvedProductValue
            {
                RawData = AkeneoTestData.Json(rawJson),
                DisplayValues = displayValues ?? Array.Empty<string>(),
                DisplayValue = displayValues == null ? string.Empty : string.Join(", ", displayValues)
            }
        };
}
