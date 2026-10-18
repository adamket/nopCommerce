using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Binding;
using Nop.Core.Domain.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

/// <summary>
/// Akeneo categories are bound to the nopCommerce categories the migration
/// created them from: same normalized name, same parent.
/// </summary>
[TestFixture]
public class AkeneoCategoryBindingPlannerTests
{
    private static readonly Dictionary<string, int> NoMappings = new();

    [TestCase("Trees & Shrubs", "trees_shrubs")]
    [TestCase("  Fruit Trees ", "fruit_trees")]
    [TestCase("Gifts/Cards (2024)", "gifts_cards_2024")]
    [TestCase("***", null)]
    public void Codes_follow_the_migration_rule(string name, string expected) =>
        Assert.That(AkeneoCategoryBindingPlanner.CodeFor(name), Is.EqualTo(expected));

    [Test]
    public void Binds_top_level_and_child_categories_by_name_and_parent()
    {
        var plans = Plan(
            [Akeneo("ecommerce", null), Akeneo("trees", "ecommerce"), Akeneo("fruit_trees", "trees")],
            [Nop(10, "Trees", 0), Nop(11, "Fruit Trees", 10)]);

        Assert.That(plans.Select(p => (p.AkeneoCode, p.Status, p.NopCategoryId)), Is.EquivalentTo(new[]
        {
            ("trees", AkeneoBindingStatus.Matched, (int?)10),
            ("fruit_trees", AkeneoBindingStatus.Matched, (int?)11)
        }));
    }

    [Test]
    public void Tree_roots_are_not_bound()
    {
        var plans = Plan([Akeneo("ecommerce", null)], [Nop(1, "Ecommerce", 0)]);

        Assert.That(plans, Is.Empty);
    }

    [Test]
    public void Existing_mappings_are_kept_and_never_rebound()
    {
        var plans = AkeneoCategoryBindingPlanner.Plan(
            [Akeneo("ecommerce", null), Akeneo("trees", "ecommerce")],
            [Nop(10, "Trees", 0), Nop(99, "Something else", 0)],
            new Dictionary<string, int> { ["trees"] = 99 });

        Assert.That(plans.Single().Status, Is.EqualTo(AkeneoBindingStatus.AlreadyBound));
        Assert.That(plans.Single().NopCategoryId, Is.EqualTo(99));
    }

    [Test]
    public void A_mapping_to_a_deleted_category_is_reported_not_replaced()
    {
        var plans = AkeneoCategoryBindingPlanner.Plan(
            [Akeneo("ecommerce", null), Akeneo("trees", "ecommerce")],
            [Nop(10, "Trees", 0), Nop(99, "Trees", 0, deleted: true)],
            new Dictionary<string, int> { ["trees"] = 99 });

        Assert.That(plans.Single().Status, Is.EqualTo(AkeneoBindingStatus.Issue));
    }

    [Test]
    public void A_category_with_no_nopcommerce_namesake_is_unmatched()
    {
        var plans = Plan([Akeneo("ecommerce", null), Akeneo("akeneo_only", "ecommerce")], [Nop(10, "Trees", 0)]);

        Assert.That(plans.Single().Status, Is.EqualTo(AkeneoBindingStatus.Unmatched));
    }

    [Test]
    public void A_namesake_under_a_different_parent_is_reported()
    {
        var plans = Plan(
            [Akeneo("ecommerce", null), Akeneo("shrubs", "ecommerce"), Akeneo("evergreen", "shrubs")],
            [Nop(20, "Shrubs", 0), Nop(30, "Trees", 0), Nop(31, "Evergreen", 30)]);

        var evergreen = plans.Single(p => p.AkeneoCode == "evergreen");
        Assert.That(evergreen.Status, Is.EqualTo(AkeneoBindingStatus.Issue));
        Assert.That(evergreen.Message, Does.Contain("different parent"));
    }

    [Test]
    public void Several_namesakes_bind_only_when_exactly_one_is_published()
    {
        var oneLive = Plan(
            [Akeneo("ecommerce", null), Akeneo("gifts", "ecommerce")],
            [Nop(40, "Gifts", 0, published: false), Nop(41, "Gifts", 0)]);
        var bothLive = Plan(
            [Akeneo("ecommerce", null), Akeneo("gifts", "ecommerce")],
            [Nop(40, "Gifts", 0), Nop(41, "GIFTS", 0)]);

        Assert.That(oneLive.Single().NopCategoryId, Is.EqualTo(41));
        Assert.That(bothLive.Single().Status, Is.EqualTo(AkeneoBindingStatus.Conflict));
    }

    [Test]
    public void A_category_already_bound_to_another_code_is_not_bound_twice()
    {
        var plans = AkeneoCategoryBindingPlanner.Plan(
            [Akeneo("ecommerce", null), Akeneo("trees", "ecommerce"), Akeneo("old_trees", "ecommerce")],
            [Nop(10, "Trees", 0)],
            new Dictionary<string, int> { ["old_trees"] = 10 });

        Assert.That(plans.Single(p => p.AkeneoCode == "trees").Status, Is.EqualTo(AkeneoBindingStatus.Issue));
    }

    private static IReadOnlyList<AkeneoCategoryBindingPlan> Plan(
        AkeneoCategoryDefinition[] akeneo,
        Category[] nop) =>
        AkeneoCategoryBindingPlanner.Plan(akeneo, nop, NoMappings);

    private static AkeneoCategoryDefinition Akeneo(string code, string parent) =>
        new() { Code = code, Parent = parent };

    private static Category Nop(int id, string name, int parentId, bool published = true, bool deleted = false) =>
        new() { Id = id, Name = name, ParentCategoryId = parentId, Published = published, Deleted = deleted };
}
