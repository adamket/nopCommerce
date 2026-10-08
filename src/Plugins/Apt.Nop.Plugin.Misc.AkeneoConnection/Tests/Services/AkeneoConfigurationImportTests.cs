using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Transactions;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Nop.Core.Caching;
using Nop.Core.Domain.Catalog;
using Nop.Data;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

[TestFixture]
public class AkeneoConfigurationImportTests
{
    private Mock<IRepository<AkeneoFamilyMapping>> _families = null!;
    private Mock<IRepository<AkeneoFamilyVariantAxisMapping>> _axes = null!;
    private Mock<IRepository<AkeneoFamilySubModelRule>> _rules = null!;
    private Mock<IRepository<AkeneoAttributeMapping>> _attributes = null!;
    private Mock<IRepository<AkeneoAttributeMappingFallbackSource>> _fallbacks = null!;
    private Mock<IRepository<AkeneoAssetMapping>> _assets = null!;
    private Mock<IRepository<AkeneoNopEntityMapping>> _entities = null!;
    private Mock<IAkeneoSyncLeaseService> _leases = null!;
    private Mock<IStaticCacheManager> _cache = null!;
    private AkeneoConfigurationImportService _service = null!;

    private static JsonObject Document() => JsonNode.Parse("""
        {
          "metadata":{"format":"akeneo-connection-diagnostic-configuration","formatVersion":1,"pluginSystemName":"Apt.Nop.Plugin.Misc.AkeneoConnection"},
          "familyMappings":[],"attributeMappings":[],"assetMappings":[],"categoryMappings":[],
          "connectionSettings":{"akeneoConnectionPassword":"[REDACTED]"},"syncProfiles":[]
        }
        """)!.AsObject();

    [SetUp]
    public void Setup()
    {
        _families = new(); _axes = new(); _rules = new(); _attributes = new();
        _fallbacks = new(); _assets = new(); _entities = new(); _leases = new(); _cache = new();
        _leases.Setup(l => l.TryAcquireAsync(It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(new AkeneoSyncLease { Id = 1 });
        _service = new(_families.Object, _axes.Object, _rules.Object, _attributes.Object, _fallbacks.Object,
            _assets.Object, _entities.Object, _leases.Object, _cache.Object,
            new AkeneoValueTemplateRenderer(new AkeneoProductValueResolver()),
            Mock.Of<IRepository<ProductAttribute>>(), Mock.Of<IRepository<SpecificationAttribute>>(),
            Mock.Of<IRepository<Category>>(), Mock.Of<IRepository<Manufacturer>>(), new AkeneoTargetTypeResolver());
    }

    [TestCase("familyMappings")]
    [TestCase("attributeMappings")]
    [TestCase("assetMappings")]
    [TestCase("categoryMappings")]
    public void Missing_section_is_rejected_before_any_writes(string section)
    {
        var json = Document(); json.Remove(section);
        Assert.ThrowsAsync<ArgumentException>(() => _service.ImportAsync(json.ToJsonString()));
        _families.VerifyNoOtherCalls(); _leases.VerifyNoOtherCalls();
    }

    [Test]
    public void Unsupported_version_is_rejected()
    {
        var json = Document(); json["metadata"]!["formatVersion"] = 2;
        Assert.Throws<ArgumentException>(() => AkeneoConfigurationImportDocument.Parse(json.ToJsonString()));
    }

    [Test]
    public void Non_category_identity_is_rejected()
    {
        var json = Document(); json["categoryMappings"]!.AsArray().Add(JsonSerializer.SerializeToNode(new AkeneoNopEntityMapping
        { AkeneoEntityTypeId = 10, NopEntityTypeId = 10, NopEntityId = 1, AkeneoCode = "sku" }));
        Assert.Throws<ArgumentException>(() => AkeneoConfigurationImportDocument.Parse(json.ToJsonString()));
    }

    [Test]
    public void Numeric_enum_ids_are_authoritative_over_exported_enum_aliases()
    {
        var json = Document(); json["attributeMappings"]!.AsArray().Add(JsonNode.Parse("""
        {"configuration":{"id":42,"akeneoAttributeCode":"sku","akeneoAttributeTypeId":100,"nopTargetTypeId":10,
        "nopTargetKey":"Sku","entityScopeId":6,"entityScope":"All","valueModeId":0,"valueMode":"Template"},"fallbackSources":[]}
        """));
        var mapping = AkeneoConfigurationImportDocument.Parse(json.ToJsonString()).AttributeMappings.Single().Configuration;
        Assert.That(mapping.EntityScopeId, Is.EqualTo(6)); Assert.That(mapping.ValueModeId, Is.Zero);
    }

    [Test]
    public async Task Explicit_empty_sections_clear_mappings_but_only_category_identity_rows()
    {
        Expression<Func<AkeneoNopEntityMapping, bool>>? predicate = null;
        _entities.Setup(r => r.DeleteAsync(It.IsAny<Expression<Func<AkeneoNopEntityMapping, bool>>>()))
            .Callback<Expression<Func<AkeneoNopEntityMapping, bool>>>(p => predicate = p).ReturnsAsync(0);
        await _service.ImportAsync(Document().ToJsonString());
        _families.Verify(r => r.DeleteAsync(It.IsAny<Expression<Func<AkeneoFamilyMapping, bool>>>()), Times.Once);
        Assert.That(predicate!.Compile()(new() { AkeneoEntityTypeId = 30, NopEntityTypeId = 20 }), Is.True);
        Assert.That(predicate.Compile()(new() { AkeneoEntityTypeId = 10, NopEntityTypeId = 10 }), Is.False);
        _leases.Verify(l => l.ReleaseAsync(It.IsAny<AkeneoSyncLease>()), Times.Once);
    }

    [Test]
    public async Task Recreated_parent_ids_are_used_for_children_and_fallbacks()
    {
        var json = Document();
        json["familyMappings"]!.AsArray().Add(JsonNode.Parse("""
        {"configuration":{"id":12,"akeneoFamilyCode":"nursery"},"axisMappings":[],
         "subModelRules":[{"id":13,"familyMappingId":12,"akeneoAxisAttributeCode":"root_variant","triggerValue":"seedling"}]}
        """));
        json["attributeMappings"]!.AsArray().Add(JsonNode.Parse("""
        {"configuration":{"id":22,"akeneoAttributeCode":"product_name","nopTargetTypeId":10,"nopTargetKey":"Name"},
         "fallbackSources":[{"id":23,"attributeMappingId":22,"akeneoAttributeCode":"root_variant_name"}]}
        """));
        _families.Setup(r => r.InsertAsync(It.IsAny<AkeneoFamilyMapping>(), false))
            .Callback<AkeneoFamilyMapping, bool>((m, _) => { Assert.That(m.Id, Is.Zero); m.Id = 101; }).Returns(Task.CompletedTask);
        _attributes.Setup(r => r.InsertAsync(It.IsAny<AkeneoAttributeMapping>(), false))
            .Callback<AkeneoAttributeMapping, bool>((m, _) => { Assert.That(m.Id, Is.Zero); m.Id = 202; }).Returns(Task.CompletedTask);
        await _service.ImportAsync(json.ToJsonString());
        _rules.Verify(r => r.InsertAsync(It.Is<AkeneoFamilySubModelRule>(m => m.Id == 0 && m.FamilyMappingId == 101), false), Times.Once);
        _fallbacks.Verify(r => r.InsertAsync(It.Is<AkeneoAttributeMappingFallbackSource>(m => m.Id == 0 && m.AttributeMappingId == 202), false), Times.Once);
    }

    [Test]
    public void Active_writer_prevents_deletion()
    {
        _leases.Setup(l => l.TryAcquireAsync(It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync((AkeneoSyncLease)null!);
        Assert.ThrowsAsync<InvalidOperationException>(() => _service.ImportAsync(Document().ToJsonString()));
        _families.VerifyNoOtherCalls();
    }

    [Test]
    public void Write_failure_aborts_transaction_and_releases_lease()
    {
        TransactionStatus? status = null;
        _families.Setup(r => r.DeleteAsync(It.IsAny<Expression<Func<AkeneoFamilyMapping, bool>>>()))
            .Callback(() => Transaction.Current!.TransactionCompleted += (_, e) => status = e.Transaction!.TransactionInformation.Status)
            .ThrowsAsync(new Exception("simulated database failure"));
        Assert.ThrowsAsync<Exception>(() => _service.ImportAsync(Document().ToJsonString()));
        Assert.That(status, Is.EqualTo(TransactionStatus.Aborted));
        _leases.Verify(l => l.ReleaseAsync(It.IsAny<AkeneoSyncLease>()), Times.Once);
    }
    [Test]
    public void Unknown_destination_id_is_rejected_before_deletion()
    {
        var json = Document(); json["categoryMappings"]!.AsArray().Add(JsonSerializer.SerializeToNode(new AkeneoNopEntityMapping
        { AkeneoEntityTypeId = 30, NopEntityTypeId = 20, NopEntityId = 999, AkeneoCode = "trees" }));
        Assert.ThrowsAsync<ArgumentException>(() => _service.ImportAsync(json.ToJsonString()));
        _families.VerifyNoOtherCalls(); _leases.VerifyNoOtherCalls();
    }

    [Test]
    public void Overlapping_destinations_are_rejected_but_disjoint_roles_are_allowed()
    {
        var json = Document();
        foreach (var code in new[] { "product_name", "variant_name" })
            json["attributeMappings"]!.AsArray().Add(JsonSerializer.SerializeToNode(new
            {
                configuration = new AkeneoAttributeMapping { AkeneoAttributeCode = code, NopTargetTypeId = 10, NopTargetKey = "Name", EntityScopeId = 1 },
                fallbackSources = Array.Empty<object>()
            }));
        Assert.Throws<ArgumentException>(() => AkeneoConfigurationImportDocument.Parse(json.ToJsonString()));
        json["attributeMappings"]![1]!["configuration"]!["EntityScopeId"] = 2;
        Assert.DoesNotThrow(() => AkeneoConfigurationImportDocument.Parse(json.ToJsonString()));
    }

    [Test]
    public void Cancelled_request_does_not_delete_mappings()
    {
        Assert.ThrowsAsync<OperationCanceledException>(() => _service.ImportAsync(Document().ToJsonString(), new CancellationToken(true)));
        _families.VerifyNoOtherCalls(); _leases.VerifyNoOtherCalls();
    }

    [Test]
    public async Task Existing_export_service_output_is_accepted_without_modification()
    {
        var settings = new Mock<global::Nop.Services.Configuration.ISettingService>();
        settings.Setup(s => s.LoadSettingAsync<AkeneoConnectionSettings>(0)).ReturnsAsync(new AkeneoConnectionSettings());
        var profiles = new Mock<IAkeneoSyncProfileService>();
        profiles.Setup(s => s.GetAllAkeneoSyncProfilesAsync()).ReturnsAsync(new List<AkeneoSyncProfile>());
        var families = new Mock<IAkeneoFamilyMappingService>();
        families.Setup(s => s.GetAllAsync()).ReturnsAsync(new List<AkeneoFamilyMapping>());
        var attributes = new Mock<IAkeneoAttributeMappingService>();
        attributes.Setup(s => s.GetAllAkeneoAttributeMappingsAsync()).ReturnsAsync(new List<AkeneoAttributeMapping>
        { new() { Id = 3, AkeneoAttributeCode = "sku", AkeneoAttributeTypeId = 100, NopTargetTypeId = 10, NopTargetKey = "Sku", EntityScopeId = 6 } });
        attributes.Setup(s => s.GetAllFallbackSourcesAsync()).ReturnsAsync(new List<AkeneoAttributeMappingFallbackSource>());
        var assets = new Mock<IAkeneoAssetMappingService>(); assets.Setup(s => s.GetAllAsync()).ReturnsAsync(new List<AkeneoAssetMapping>());
        var entities = new Mock<IAkeneoNopEntityMappingService>();
        entities.Setup(s => s.GetAkeneoNopEntityMappingsAsync(AkeneoEntityType.Category)).ReturnsAsync(new List<AkeneoNopEntityMapping>());
        var exporter = new AkeneoConfigurationExportService(settings.Object, profiles.Object, families.Object, attributes.Object, assets.Object, entities.Object);
        var bytes = await exporter.ExportAsync(0);
        var parsed = AkeneoConfigurationImportDocument.Parse(System.Text.Encoding.UTF8.GetString(bytes));
        Assert.That(parsed.AttributeMappings.Single().Configuration.EntityScopeId, Is.EqualTo(6));
        await _service.ImportAsync(System.Text.Encoding.UTF8.GetString(bytes));
        _attributes.Verify(r => r.InsertAsync(It.Is<AkeneoAttributeMapping>(m => m.AkeneoAttributeCode == "sku" && m.Id == 0), false), Times.Once);
    }}
