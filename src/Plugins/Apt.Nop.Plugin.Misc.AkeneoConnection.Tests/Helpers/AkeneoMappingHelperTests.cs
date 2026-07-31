using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Helpers;

[TestFixture]
public class AkeneoMappingHelperTests
{
    [Test]
    public void SetIfChanged_updates_when_string_values_differ()
    {
        var value = "old";

        var changed = AkeneoMappingHelper.SetIfChanged(value, "new", updated => value = updated);

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.True);
            Assert.That(value, Is.EqualTo("new"));
        });
    }

    [Test]
    public void SetIfChanged_treats_null_and_empty_strings_as_equal()
    {
        string? value = null;

        var changed = AkeneoMappingHelper.SetIfChanged(value, string.Empty, updated => value = updated);

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.False);
            Assert.That(value, Is.Null);
        });
    }

    [Test]
    public void GetSourceMappingCode_uses_attribute_code_for_normal_mapping()
    {
        var mapping = new AkeneoAttributeMapping
        {
            AkeneoAttributeCode = " product_name ",
            AkeneoAttributeTypeId = (int)AkeneoAttributeType.Text
        };

        Assert.That(AkeneoMappingHelper.GetSourceMappingCode(mapping), Is.EqualTo("product_name"));
    }

    [Test]
    public void GetSourceMappingCode_includes_reference_entity_field()
    {
        var mapping = new AkeneoAttributeMapping
        {
            AkeneoAttributeCode = "species",
            AkeneoAttributeTypeId = (int)AkeneoAttributeType.ReferenceEntity,
            AkeneoReferenceEntityAttributeCode = " botanical_name "
        };

        Assert.That(
            AkeneoMappingHelper.GetSourceMappingCode(mapping),
            Is.EqualTo("species::botanical_name"));
    }

    [Test]
    public void GetSourceMappingCode_uses_stable_template_mapping_key()
    {
        var mapping = new AkeneoAttributeMapping
        {
            ValueMode = AkeneoAttributeMappingValueMode.Template,
            MappingKey = " product-title "
        };

        Assert.That(
            AkeneoMappingHelper.GetSourceMappingCode(mapping),
            Is.EqualTo("template::product-title"));
    }

    [Test]
    public void GetSourceMappingCode_uses_legacy_id_when_template_has_no_key()
    {
        var mapping = new AkeneoAttributeMapping
        {
            Id = 42,
            ValueMode = AkeneoAttributeMappingValueMode.Template
        };

        Assert.That(
            AkeneoMappingHelper.GetSourceMappingCode(mapping),
            Is.EqualTo("template::legacy-42"));
    }

    [TestCase(AkeneoAttributeType.ReferenceEntity, true)]
    [TestCase(AkeneoAttributeType.ReferenceEntityCollection, true)]
    [TestCase(AkeneoAttributeType.Select, false)]
    [TestCase(AkeneoAttributeType.Text, false)]
    public void IsReferenceEntityType_identifies_reference_types(
        AkeneoAttributeType type,
        bool expected)
    {
        Assert.That(AkeneoMappingHelper.IsReferenceEntityType(type), Is.EqualTo(expected));
    }
}
