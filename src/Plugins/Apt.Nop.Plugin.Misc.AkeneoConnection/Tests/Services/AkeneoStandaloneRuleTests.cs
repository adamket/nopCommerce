using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

/// <summary>
/// A matching standalone sub-model rule wins over an existing grouped or
/// associated-product child; only an attribute combination, which has no
/// product of its own, keeps its representation.
/// </summary>
[TestFixture]
public class AkeneoStandaloneRuleTests
{
    [TestCase(null, true)]
    [TestCase(AkeneoVariantRelationshipMode.None, true)]
    [TestCase(AkeneoVariantRelationshipMode.GroupedProducts, true)]
    [TestCase(AkeneoVariantRelationshipMode.AssociatedToProductAttributeValue, true)]
    [TestCase(AkeneoVariantRelationshipMode.ProductAttributeCombinations, false)]
    public void The_standalone_rule_applies_unless_the_sku_is_a_combination(AkeneoVariantRelationshipMode? existing, bool applies) =>
        Assert.That(AkeneoProductBatchSyncService.StandaloneRuleApplies(existing), Is.EqualTo(applies));
}
