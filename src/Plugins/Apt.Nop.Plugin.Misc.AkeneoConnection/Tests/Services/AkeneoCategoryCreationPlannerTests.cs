using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Binding;
using Nop.Core.Domain.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

/// <summary>
/// "Create missing categories" creates only what binding found nothing for, parents first,
/// under the parent's nopCommerce category.
/// </summary>
[TestFixture]
public class AkeneoCategoryCreationPlannerTests
{
    private static readonly Dictionary<string, int> NoMappings = new();

    [Test]
    public void An_empty_nopcommerce_catalog_gets_the_whole_tree_parents_first()
    {
        var plans = Plan(
            [
                Akeneo("ecommerce", null),
                Akeneo("pine_trees", "evergreens", "Pine Trees"),
                Akeneo("evergreens", "tree_nursery", "Evergreens"),
                Akeneo("tree_nursery", "ecommerce", "Tree Nursery")
            ],
            []);

        Assert.That(plans.Select(p => (p.AkeneoCode, p.Name, p.ParentCode, p.ParentNopCategoryId, p.CanCreate)), Is.EqualTo(new[]
        {
            ("tree_nursery", "Tree Nursery", (string)null, (int?)0, true),
            ("evergreens", "Evergreens", "tree_nursery", (int?)null, true),
            ("pine_trees", "Pine Trees", "evergreens", (int?)null, true)
        }));
    }

    [Test]
    public void A_missing_child_goes_under_its_parents_bound_category()
    {
        var plans = Plan(
            [Akeneo("ecommerce", null), Akeneo("trees", "ecommerce"), Akeneo("shade_trees", "trees", "Shade Trees")],
            [Nop(10, "Trees", 0)]);

        var creation = plans.Single();
        Assert.That(creation.AkeneoCode, Is.EqualTo("shade_trees"));
        Assert.That(creation.ParentNopCategoryId, Is.EqualTo(10));
        Assert.That(creation.CanCreate, Is.True);
    }

    [Test]
    public void A_name_found_under_another_parent_is_not_duplicated()
    {
        // Evergreen exists under Trees; Akeneo has it under Shrubs. It may have been moved, so it is
        // left for manual mapping rather than created a second time.
        var plans = Plan(
            [Akeneo("ecommerce", null), Akeneo("shrubs", "ecommerce"), Akeneo("evergreen", "shrubs")],
            [Nop(20, "Shrubs", 0), Nop(30, "Trees", 0), Nop(31, "Evergreen", 30)]);

        Assert.That(plans, Is.Empty);
    }

    [Test]
    public void Under_a_category_created_in_the_same_run_a_namesake_elsewhere_does_not_stop_creation()
    {
        // Gifts is missing, so Gifts >> Socks can't already exist; the Socks under Apparel is another category.
        var plans = Plan(
            [Akeneo("ecommerce", null), Akeneo("apparel", "ecommerce"), Akeneo("gifts", "ecommerce"), Akeneo("gifts_socks", "gifts", "Socks"), Akeneo("socks", "apparel")],
            [Nop(80, "Apparel", 0), Nop(90, "Socks", 80)]);

        Assert.That(plans.Where(p => p.CanCreate).Select(p => p.AkeneoCode), Is.EqualTo(new[] { "gifts", "gifts_socks" }));
    }

    [Test]
    public void A_category_whose_parent_cannot_be_created_or_bound_is_reported()
    {
        // The parent is a conflict (two published namesakes), so it is neither bound nor created.
        var plans = Plan(
            [Akeneo("ecommerce", null), Akeneo("gifts", "ecommerce"), Akeneo("cards", "gifts", "Cards")],
            [Nop(1, "Gifts", 0), Nop(2, "GIFTS", 0)]);

        var cards = plans.Single();
        Assert.That(cards.AkeneoCode, Is.EqualTo("cards"));
        Assert.That(cards.CanCreate, Is.False);
        Assert.That(cards.Reason, Does.Contain("parent 'gifts'"));
    }

    [Test]
    public void A_mapping_to_a_deleted_category_is_reported_and_not_recreated()
    {
        var mappings = new Dictionary<string, int> { ["trees"] = 99 };
        var akeneo = new[] { Akeneo("ecommerce", null), Akeneo("trees", "ecommerce", "Trees") };
        var nop = new[] { Nop(99, "Trees", 0, deleted: true) };

        var plans = AkeneoCategoryCreationPlanner.Plan(akeneo, AkeneoCategoryBindingPlanner.Plan(akeneo, nop, mappings), mappings);

        var trees = plans.Single();
        Assert.That(trees.CanCreate, Is.False);
        Assert.That(trees.Reason, Does.Contain("Category Mappings"));
    }

    [Test]
    public void Bound_categories_are_never_created()
    {
        var plans = Plan(
            [Akeneo("ecommerce", null), Akeneo("trees", "ecommerce"), Akeneo("fruit", "trees")],
            [Nop(10, "Trees", 0), Nop(11, "Fruit", 10)]);

        Assert.That(plans, Is.Empty);
    }

    [Test]
    public void A_category_without_a_label_is_named_after_its_code()
    {
        var plans = Plan([Akeneo("ecommerce", null), Akeneo("new_arrivals", "ecommerce")], []);

        Assert.That(plans.Single().Name, Is.EqualTo("new_arrivals"));
    }

    private static IReadOnlyList<AkeneoCategoryCreationPlan> Plan(
        AkeneoCategoryDefinition[] akeneo,
        Category[] nop) =>
        AkeneoCategoryCreationPlanner.Plan(akeneo, AkeneoCategoryBindingPlanner.Plan(akeneo, nop, NoMappings), NoMappings);

    private static AkeneoCategoryDefinition Akeneo(string code, string parent, string label = null) =>
        new()
        {
            Code = code,
            Parent = parent,
            Labels = label == null ? new() : new() { ["en_US"] = label }
        };

    private static Category Nop(int id, string name, int parentId, bool published = true, bool deleted = false) =>
        new() { Id = id, Name = name, ParentCategoryId = parentId, Published = published, Deleted = deleted };
}
