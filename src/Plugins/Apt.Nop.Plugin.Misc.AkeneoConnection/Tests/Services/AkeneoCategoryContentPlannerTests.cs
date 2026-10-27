using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Categories;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

/// <summary>
/// Category content sync: reading Akeneo category template values, choosing
/// the value a mapping reads, and deciding text changes.
/// </summary>
[TestFixture]
public class AkeneoCategoryContentPlannerTests
{
    // The shape Akeneo returns for GET /categories?with_enriched_attributes=true.
    private const string ShadeTreesJson = """
        {
          "code": "shade_trees",
          "parent": "trees",
          "updated": "2026-10-20T14:00:00+00:00",
          "labels": { "en_US": "Shade Trees", "es_ES": "Árboles de sombra" },
          "values": {
            "description|96b88bf4-c2b7-4b64-a1f9-5d4876c02c26|ecommerce|en_US": {
              "data": "<p>Shade trees cool your home.</p>",
              "type": "richtext",
              "channel": "ecommerce",
              "locale": "en_US",
              "attribute_code": "description|96b88bf4-c2b7-4b64-a1f9-5d4876c02c26"
            },
            "meta_title|11111111-2222-3333-4444-555555555555|ecommerce|en_US": {
              "data": "  Shade Trees for Sale  ",
              "type": "text",
              "channel": "ecommerce",
              "locale": "en_US",
              "attribute_code": "meta_title|11111111-2222-3333-4444-555555555555"
            },
            "url_slug|66666666-7777-8888-9999-000000000000|ecommerce|en_US": {
              "data": "",
              "type": "text",
              "channel": "ecommerce",
              "locale": "en_US",
              "attribute_code": "url_slug|66666666-7777-8888-9999-000000000000"
            },
            "image|8587cda6-58c8-47fa-9278-033e1d8c735c": {
              "data": {
                "size": 168107,
                "extension": "jpg",
                "file_path": "8/8/3/d/883d041fc9f22ce42fee07d96c05b0b7ec7e66de_shade.jpg",
                "mime_type": "image/jpeg",
                "original_filename": "shade.jpg"
              },
              "type": "image",
              "channel": null,
              "locale": null,
              "attribute_code": "image|8587cda6-58c8-47fa-9278-033e1d8c735c"
            }
          }
        }
        """;

    private static AkeneoCategoryDefinition ShadeTrees() =>
        JsonSerializer.Deserialize<AkeneoCategoryDefinition>(ShadeTreesJson)!;

    private static AkeneoCategoryContentSource Resolve(AkeneoCategoryContentMapping mapping)
    {
        var category = ShadeTrees();
        return AkeneoCategoryContentPlanner.ResolveSource(mapping, category, AkeneoCategoryValueReader.Read(category.Values));
    }

    private static AkeneoCategoryContentMapping Attribute(
        AkeneoCategoryContentTarget target,
        string code,
        string locale = "en_US",
        string channel = "ecommerce") => new()
        {
            Target = target,
            SourceType = AkeneoCategoryContentSourceType.TemplateAttribute,
            AkeneoAttributeCode = code,
            Locale = locale,
            Channel = channel
        };

    [Test]
    public void Values_are_read_by_bare_attribute_code_with_their_scope()
    {
        var values = AkeneoCategoryValueReader.Read(ShadeTrees().Values);

        Assert.That(values.Select(value => (value.AttributeCode, value.Locale, value.Channel)), Is.EquivalentTo(new[]
        {
            ("description", "en_US", "ecommerce"),
            ("meta_title", "en_US", "ecommerce"),
            ("url_slug", "en_US", "ecommerce"),
            ("image", (string)null, (string)null)
        }));

        var image = values.Single(value => value.AttributeCode == "image");
        Assert.Multiple(() =>
        {
            Assert.That(image.IsImage, Is.True);
            Assert.That(image.ImageFilePath, Does.EndWith("_shade.jpg"));
            Assert.That(image.ImageFileName, Is.EqualTo("shade.jpg"));
            Assert.That(image.ImageMimeType, Is.EqualTo("image/jpeg"));
        });
    }

    [Test]
    public void The_array_form_used_when_writing_is_read_too()
    {
        using var document = JsonDocument.Parse("""
            [ { "attribute_code": "description|abc", "data": "Hello", "locale": "en_US", "channel": null } ]
            """);

        var value = AkeneoCategoryValueReader.Read(document.RootElement).Single();

        Assert.That((value.AttributeCode, value.Text, value.Locale, value.Channel),
            Is.EqualTo(("description", "Hello", "en_US", (string)null)));
    }

    [Test]
    public void A_category_read_without_enriched_attributes_has_no_values()
    {
        var category = JsonSerializer.Deserialize<AkeneoCategoryDefinition>("""{ "code": "trees", "labels": {} }""")!;

        Assert.That(AkeneoCategoryValueReader.Read(category.Values), Is.Empty);
    }

    [Test]
    public void A_description_keeps_its_html_exactly()
    {
        var source = Resolve(Attribute(AkeneoCategoryContentTarget.Description, "description"));

        Assert.That(source.Text, Is.EqualTo("<p>Shade trees cool your home.</p>"));
    }

    [Test]
    public void One_line_values_are_trimmed()
    {
        var source = Resolve(Attribute(AkeneoCategoryContentTarget.MetaTitle, "META_TITLE"));

        Assert.That(source.Text, Is.EqualTo("Shade Trees for Sale"));
    }

    [Test]
    public void A_value_in_another_locale_or_channel_is_not_used()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Resolve(Attribute(AkeneoCategoryContentTarget.MetaTitle, "meta_title", locale: "es_ES")).HasValue, Is.False);
            Assert.That(Resolve(Attribute(AkeneoCategoryContentTarget.MetaTitle, "meta_title", channel: "print")).HasValue, Is.False);
        });
    }

    [Test]
    public void An_empty_value_is_no_value()
    {
        var source = Resolve(Attribute(AkeneoCategoryContentTarget.SeName, "url_slug"));

        Assert.That((source.HasValue, source.Problem), Is.EqualTo((false, (string)null)));
    }

    [Test]
    public void The_picture_comes_from_a_non_scoped_image_value()
    {
        var source = Resolve(Attribute(AkeneoCategoryContentTarget.Picture, "image", locale: null, channel: null));

        Assert.That(source.Image?.ImageFileName, Is.EqualTo("shade.jpg"));
    }

    [Test]
    public void Text_and_images_cannot_be_swapped()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Resolve(Attribute(AkeneoCategoryContentTarget.Description, "image", null, null)).Problem,
                Does.Contain("is an image"));
            Assert.That(Resolve(Attribute(AkeneoCategoryContentTarget.Picture, "description")).Problem,
                Does.Contain("is not an image"));
        });
    }

    [Test]
    public void The_label_is_read_in_the_mapping_locale()
    {
        var mapping = new AkeneoCategoryContentMapping
        {
            Target = AkeneoCategoryContentTarget.Name,
            SourceType = AkeneoCategoryContentSourceType.Label,
            Locale = "es_ES"
        };

        Assert.That(Resolve(mapping).Text, Is.EqualTo("Árboles de sombra"));
    }

    [TestCase("Old", "New", true, "New")]
    [TestCase("Same", "Same", false, "Same")]
    [TestCase("same", "Same", true, "Same")]
    public void Akeneo_overwrites_a_different_nopcommerce_value(string current, string desired, bool change, string value)
    {
        var decision = AkeneoCategoryContentPlanner.DecideText(
            AkeneoCategoryContentTarget.MetaTitle,
            AkeneoCategoryContentEmptyValueHandling.Keep,
            current,
            desired);

        Assert.That((decision.Change, decision.Value), Is.EqualTo((change, value)));
    }

    [Test]
    public void An_empty_akeneo_value_keeps_or_clears_as_mapped()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AkeneoCategoryContentPlanner.DecideText(
                AkeneoCategoryContentTarget.Description, AkeneoCategoryContentEmptyValueHandling.Keep, "Edited in nop", null).Change,
                Is.False);
            Assert.That(AkeneoCategoryContentPlanner.DecideText(
                AkeneoCategoryContentTarget.Description, AkeneoCategoryContentEmptyValueHandling.Clear, "Edited in nop", " "),
                Is.EqualTo(new AkeneoCategoryContentTextDecision(true, string.Empty)));
            Assert.That(AkeneoCategoryContentPlanner.DecideText(
                AkeneoCategoryContentTarget.Description, AkeneoCategoryContentEmptyValueHandling.Clear, null, null).Change,
                Is.False);
        });
    }

    [Test]
    public void A_name_is_never_cleared()
    {
        var decision = AkeneoCategoryContentPlanner.DecideText(
            AkeneoCategoryContentTarget.Name,
            AkeneoCategoryContentEmptyValueHandling.Clear,
            "Shade Trees",
            null);

        Assert.That(decision.Change, Is.False);
    }

    [Test]
    public void Validation_rejects_impossible_mappings()
    {
        var existing = new AkeneoCategoryContentMapping
        {
            Id = 1,
            Target = AkeneoCategoryContentTarget.Description,
            SourceType = AkeneoCategoryContentSourceType.TemplateAttribute,
            AkeneoAttributeCode = "description"
        };

        Assert.Multiple(() =>
        {
            Assert.That(AkeneoCategoryContentPlanner.Validate(new AkeneoCategoryContentMapping
            {
                Target = AkeneoCategoryContentTarget.Picture,
                SourceType = AkeneoCategoryContentSourceType.TemplateAttribute,
                AkeneoAttributeCode = "image",
                LanguageId = 2
            }, []), Has.Some.Contains("one picture for all languages"));

            Assert.That(AkeneoCategoryContentPlanner.Validate(new AkeneoCategoryContentMapping
            {
                Target = AkeneoCategoryContentTarget.Name,
                SourceType = AkeneoCategoryContentSourceType.Label,
                Locale = "en_US",
                EmptyValueHandling = AkeneoCategoryContentEmptyValueHandling.Clear
            }, []), Has.Some.Contains("cannot be cleared"));

            Assert.That(AkeneoCategoryContentPlanner.Validate(new AkeneoCategoryContentMapping
            {
                Target = AkeneoCategoryContentTarget.Name,
                SourceType = AkeneoCategoryContentSourceType.Label
            }, []), Has.Some.Contains("locale of the Akeneo label"));

            Assert.That(AkeneoCategoryContentPlanner.Validate(new AkeneoCategoryContentMapping
            {
                Id = 2,
                Target = AkeneoCategoryContentTarget.Description,
                SourceType = AkeneoCategoryContentSourceType.Label,
                Locale = "en_US"
            }, [existing]), Has.Some.Contains("already mapped"));

            // The same property for another language is a separate mapping.
            Assert.That(AkeneoCategoryContentPlanner.Validate(new AkeneoCategoryContentMapping
            {
                Id = 3,
                Target = AkeneoCategoryContentTarget.Description,
                SourceType = AkeneoCategoryContentSourceType.TemplateAttribute,
                AkeneoAttributeCode = "description",
                Locale = "es_ES",
                LanguageId = 2
            }, [existing]), Is.Empty);
        });
    }

    [Test]
    public void Template_attributes_are_listed_with_the_scopes_their_values_use()
    {
        var trees = JsonSerializer.Deserialize<AkeneoCategoryDefinition>("""
            {
              "code": "trees",
              "values": {
                "description|a|ecommerce|es_ES": { "data": "Árboles", "type": "richtext", "channel": "ecommerce", "locale": "es_ES", "attribute_code": "description|a" }
              }
            }
            """)!;

        var attributes = AkeneoCategoryMappingModelFactory.DescribeTemplateAttributes([ShadeTrees(), trees]);

        var description = attributes.Single(attribute => attribute.Code == "description");
        Assert.Multiple(() =>
        {
            Assert.That(attributes.Select(attribute => attribute.Code), Is.EqualTo(new[] { "description", "image", "meta_title", "url_slug" }));
            Assert.That(description.CategoryCount, Is.EqualTo(2));
            Assert.That(description.Scopes.Select(scope => scope.Locale), Is.EqualTo(new[] { "en_US", "es_ES" }));
            Assert.That(attributes.Single(attribute => attribute.Code == "image").IsImage, Is.True);
        });
    }
}
