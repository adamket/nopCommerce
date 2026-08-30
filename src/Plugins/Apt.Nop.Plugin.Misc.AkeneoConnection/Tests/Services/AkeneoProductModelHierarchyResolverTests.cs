using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.TestData;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

[TestFixture]
public class AkeneoProductModelHierarchyResolverTests
{
    private AkeneoProductModelHierarchyResolver _resolver = null!;

    [SetUp]
    public void SetUp() => _resolver = new AkeneoProductModelHierarchyResolver();

    [Test]
    public void Resolve_immediate_parent_mode_selects_submodel_and_preserves_leaf_as_effective_leaf()
    {
        var leaf = Product("leaf", "submodel", """
        {
          "name": [{"locale":"en_US","scope":"ecommerce","data":"Leaf name"}]
        }
        """);
        var submodel = Model("submodel", "root", """
        {
          "root_type": [{"locale":null,"scope":null,"data":"bare_root"}]
        }
        """);
        var root = Model("root", null, """
        {
          "brand": [{"locale":null,"scope":null,"data":"Arbor Day"}]
        }
        """);

        var result = _resolver.Resolve(
            leaf,
            new[] { submodel, root },
            AkeneoProductModelHierarchyMode.ImmediateParentProductModel);

        Assert.Multiple(() =>
        {
            Assert.That(result.ParentProductModel.Code, Is.EqualTo("submodel"));
            Assert.That(result.EffectiveLeaf, Is.SameAs(leaf));
            Assert.That(result.IsFlattened, Is.False);
            Assert.That(ReadData(result.EffectiveParentProductModel.Values, "root_type"), Is.EqualTo("bare_root"));
            Assert.That(ReadData(result.EffectiveParentProductModel.Values, "brand"), Is.EqualTo("Arbor Day"));
            Assert.That(ReadData(result.LeafWithInheritedValues.Values, "brand"), Is.EqualTo("Arbor Day"));
        });
    }

    [Test]
    public void Resolve_root_mode_selects_root_and_uses_inherited_leaf_values()
    {
        var leaf = Product("leaf", "submodel", """
        {
          "name": [{"locale":"en_US","scope":"ecommerce","data":"Leaf name"}]
        }
        """);
        var submodel = Model("submodel", "root", """
        {
          "root_type": [{"locale":null,"scope":null,"data":"bare_root"}]
        }
        """);
        var root = Model("root", null, """
        {
          "brand": [{"locale":null,"scope":null,"data":"Arbor Day"}]
        }
        """);

        var result = _resolver.Resolve(
            leaf,
            new[] { submodel, root },
            AkeneoProductModelHierarchyMode.RootProductModel);

        Assert.Multiple(() =>
        {
            Assert.That(result.ParentProductModel.Code, Is.EqualTo("root"));
            Assert.That(result.EffectiveParentProductModel.Code, Is.EqualTo("root"));
            Assert.That(result.EffectiveLeaf, Is.SameAs(result.LeafWithInheritedValues));
            Assert.That(result.IsFlattened, Is.True);
            Assert.That(ReadData(result.EffectiveLeaf.Values, "root_type"), Is.EqualTo("bare_root"));
            Assert.That(ReadData(result.EffectiveLeaf.Values, "brand"), Is.EqualTo("Arbor Day"));
        });
    }

    [Test]
    public void Resolve_child_entry_wins_but_missing_locale_scope_entries_are_inherited()
    {
        var leaf = Product("leaf", "submodel", """
        {
          "description": [
            {"locale":"en_US","scope":"ecommerce","data":"Leaf EN"}
          ]
        }
        """);
        var parent = Model("submodel", null, """
        {
          "description": [
            {"locale":"en_US","scope":"ecommerce","data":"Parent EN"},
            {"locale":"es_ES","scope":"ecommerce","data":"Parent ES"},
            {"locale":"en_US","scope":"print","data":"Parent print"}
          ]
        }
        """);

        var result = _resolver.Resolve(
            leaf,
            new[] { parent },
            AkeneoProductModelHierarchyMode.ImmediateParentProductModel);

        var entries = result.LeafWithInheritedValues.Values
            .GetProperty("description")
            .EnumerateArray()
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(entries, Has.Count.EqualTo(3));
            Assert.That(FindData(entries, "en_US", "ecommerce"), Is.EqualTo("Leaf EN"));
            Assert.That(FindData(entries, "es_ES", "ecommerce"), Is.EqualTo("Parent ES"));
            Assert.That(FindData(entries, "en_US", "print"), Is.EqualTo("Parent print"));
        });
    }

    [Test]
    public void Resolve_requires_an_immediate_parent_model()
    {
        Assert.That(
            () => _resolver.Resolve(
                Product("leaf", null, "{}"),
                Array.Empty<AkeneoProductDefinition>(),
                AkeneoProductModelHierarchyMode.ImmediateParentProductModel),
            Throws.TypeOf<InvalidOperationException>());
    }

    private static AkeneoProductDefinition Product(string identifier, string? parent, string values) =>
        AkeneoTestData.Product(identifier: identifier, parent: parent, valuesJson: values);

    private static AkeneoProductDefinition Model(string code, string? parent, string values) =>
        AkeneoTestData.Product(identifier: null, code: code, parent: parent, valuesJson: values, family: null);

    private static string? ReadData(JsonElement values, string attribute) =>
        values.GetProperty(attribute)[0].GetProperty("data").GetString();

    private static string? FindData(
        IEnumerable<JsonElement> entries,
        string locale,
        string scope) =>
        entries.Single(entry =>
                entry.GetProperty("locale").GetString() == locale &&
                entry.GetProperty("scope").GetString() == scope)
            .GetProperty("data")
            .GetString();
}
