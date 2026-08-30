using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Helpers;

[TestFixture]
public class AkeneoSyncScopeHasherTests
{
    [Test]
    public void Build_is_stable_for_equivalent_code_order_casing_and_whitespace()
    {
        var first = CreateProfile();
        first.AkeneoLocales = " en_US,es_ES ";
        first.AkeneoFamilyCodes = "Trees,Shrubs";
        first.AkeneoCategoryCodes = " Shade,Evergreen ";
        first.AkeneoProductGroupCodes = "Web,Retail";

        var second = CreateProfile();
        second.AkeneoLocales = "es_es,EN_us";
        second.AkeneoFamilyCodes = " shrubs,trees,TREES ";
        second.AkeneoCategoryCodes = "evergreen,shade";
        second.AkeneoProductGroupCodes = "retail,web";

        Assert.That(AkeneoSyncScopeHasher.Build(second), Is.EqualTo(AkeneoSyncScopeHasher.Build(first)));
    }

    [Test]
    public void Build_is_stable_for_additional_json_whitespace_changes()
    {
        var first = CreateProfile();
        first.AdditionalSearchJson = "{\"family\":[{\"operator\":\"IN\",\"value\":[\"trees\"]}]}";

        var second = CreateProfile();
        second.AdditionalSearchJson = """
        {
          "family": [ { "operator": "IN", "value": [ "trees" ] } ]
        }
        """;

        Assert.That(AkeneoSyncScopeHasher.Build(second), Is.EqualTo(AkeneoSyncScopeHasher.Build(first)));
    }

    [Test]
    public void Build_changes_when_source_scope_changes()
    {
        var first = CreateProfile();
        var second = CreateProfile();
        second.ProductEnabledFilterId = (int)AkeneoProductEnabledFilter.DisabledOnly;

        Assert.That(AkeneoSyncScopeHasher.Build(second), Is.Not.EqualTo(AkeneoSyncScopeHasher.Build(first)));
    }

    [Test]
    public void Build_does_not_change_for_destination_write_policy_changes()
    {
        var first = CreateProfile();
        var second = CreateProfile();
        second.CategorySyncModeId = (int)AkeneoCollectionSyncMode.ReplaceAll;
        second.ProductWriteModeId = (int)AkeneoProductWriteMode.CreateOnly;

        Assert.That(AkeneoSyncScopeHasher.Build(second), Is.EqualTo(AkeneoSyncScopeHasher.Build(first)));
    }

    private static AkeneoSyncProfile CreateProfile() => new()
    {
        AkeneoChannel = "ecommerce",
        AkeneoLocales = "en_US",
        AkeneoFamilyCodes = "trees",
        AkeneoCategoryCodes = "",
        AkeneoProductGroupCodes = "",
        CategoryFilterModeId = (int)AkeneoCategoryFilterMode.None,
        ProductEnabledFilterId = (int)AkeneoProductEnabledFilter.Any,
        ProductParentFilterModeId = (int)AkeneoProductParentFilterMode.Any
    };
}
