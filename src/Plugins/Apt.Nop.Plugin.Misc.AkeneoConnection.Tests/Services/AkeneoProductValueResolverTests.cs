using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.TestData;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

[TestFixture]
public class AkeneoProductValueResolverTests
{
    private AkeneoProductValueResolver _resolver = null!;

    [SetUp]
    public void SetUp() => _resolver = new AkeneoProductValueResolver();

    [Test]
    public void TryGetValue_resolves_root_fields_without_values_payload()
    {
        var product = AkeneoTestData.Product(identifier: "RM-1", code: "red_maple");

        var found = _resolver.TryGetValue(product, "sku", out var resolved);

        Assert.Multiple(() =>
        {
            Assert.That(found, Is.True);
            Assert.That(resolved.DisplayValue, Is.EqualTo("RM-1"));
            Assert.That(resolved.RawData!.Value.GetString(), Is.EqualTo("RM-1"));
        });
    }

    [Test]
    public void TryGetValue_prefers_exact_locale_and_channel()
    {
        var values = "{\"name\":[" +
                     AkeneoTestData.ValueEntry("\"Global\"") + "," +
                     AkeneoTestData.ValueEntry("\"US Web\"", "en_US", "ecommerce") + "," +
                     AkeneoTestData.ValueEntry("\"US Print\"", "en_US", "print") +
                     "]}";
        var product = AkeneoTestData.Product(valuesJson: values);

        var found = _resolver.TryGetValue(
            product,
            "name",
            out var resolved,
            locale: "en-US",
            channel: "ecommerce");

        Assert.Multiple(() =>
        {
            Assert.That(found, Is.True);
            Assert.That(resolved.DisplayValue, Is.EqualTo("US Web"));
            Assert.That(resolved.Locale, Is.EqualTo("en_US"));
            Assert.That(resolved.Channel, Is.EqualTo("ecommerce"));
        });
    }

    [Test]
    public void TryGetValue_falls_back_to_unscoped_value_for_requested_context()
    {
        var values = "{\"name\":[" +
                     AkeneoTestData.ValueEntry("\"Global\"") + "," +
                     AkeneoTestData.ValueEntry("\"French\"", "fr_FR", "ecommerce") +
                     "]}";
        var product = AkeneoTestData.Product(valuesJson: values);

        var found = _resolver.TryGetValue(
            product,
            "name",
            out var resolved,
            locale: "en_US",
            channel: "ecommerce");

        Assert.Multiple(() =>
        {
            Assert.That(found, Is.True);
            Assert.That(resolved.DisplayValue, Is.EqualTo("Global"));
        });
    }

    [Test]
    public void TryGetValue_uses_localized_linked_data_label_for_select()
    {
        var additional = """
        "linked_data": {
          "bare_root": {
            "labels": {
              "en_US": "Bare Root",
              "es_ES": "Raíz desnuda"
            }
          }
        }
        """;
        var values = "{\"root_type\":[" +
                     AkeneoTestData.ValueEntry("\"bare_root\"", "es_ES", null, additional) +
                     "]}";
        var product = AkeneoTestData.Product(valuesJson: values);

        _resolver.TryGetValue(product, "root_type", out var resolved, locale: "es-ES");

        Assert.That(resolved.DisplayValue, Is.EqualTo("Raíz desnuda"));
    }

    [Test]
    public void TryGetValue_preserves_multiselect_code_label_order()
    {
        var additional = """
        "linked_data": {
          "sun": { "labels": { "en_US": "Sun" } },
          "shade": { "labels": { "en_US": "Shade" } }
        }
        """;
        var values = "{\"exposure\":[" +
                     AkeneoTestData.ValueEntry("[\"shade\",\"sun\"]", null, null, additional) +
                     "]}";
        var product = AkeneoTestData.Product(valuesJson: values);

        _resolver.TryGetValue(product, "exposure", out var resolved, locale: "en_US");

        Assert.Multiple(() =>
        {
            Assert.That(resolved.DisplayValues, Is.EqualTo(new[] { "Shade", "Sun" }));
            Assert.That(resolved.DisplayValue, Is.EqualTo("Shade, Sun"));
        });
    }

    [Test]
    public void TryGetValue_selects_requested_currency_from_price_collection()
    {
        var prices = "[{\"amount\":\"12.50\",\"currency\":\"USD\"}," +
                     "{\"amount\":\"18.00\",\"currency\":\"CAD\"}]";
        var values = "{\"price\":[" + AkeneoTestData.ValueEntry(prices) + "]}";
        var product = AkeneoTestData.Product(valuesJson: values);

        _resolver.TryGetValue(product, "price", out var resolved, currency: "CAD");

        Assert.That(resolved.DisplayValue, Is.EqualTo("18.00"));
    }

    [Test]
    public void TryGetValue_formats_metric_values()
    {
        var metric = "{\"amount\":\"12\",\"unit\":\"INCH\"}";
        var values = "{\"height\":[" + AkeneoTestData.ValueEntry(metric) + "]}";
        var product = AkeneoTestData.Product(valuesJson: values);

        _resolver.TryGetValue(product, "height", out var resolved);

        Assert.That(resolved.DisplayValue, Is.EqualTo("12 INCH"));
    }

    [Test]
    public void TryGetValue_returns_false_when_only_mismatched_scoped_values_exist()
    {
        var values = "{\"name\":[" +
                     AkeneoTestData.ValueEntry("\"French\"", "fr_FR", "print") +
                     "]}";
        var product = AkeneoTestData.Product(valuesJson: values);

        var found = _resolver.TryGetValue(
            product,
            "name",
            out _,
            locale: "en_US",
            channel: "ecommerce");

        Assert.That(found, Is.False);
    }
}
