using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core.Domain.Catalog;
using Nop.Data;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

[TestFixture]
public class AkeneoCoreChangeLogTests
{
    [Test, SetCulture("fr-FR")]
    public async Task Updated_fields_include_their_actual_previous_and_saved_values()
    {
        var product = Product();
        var context = Context(product, Mapping("Name", "Red Maple Seedling"), Mapping("Price", "15.75"), Mapping("Published", "false"), Mapping("Sku", "SKU"));
        await Synchronizer().SynchronizeAsync(context);
        var message = context.Result.Messages.Single(message => message.StartsWith("Updated core product fields"));
        Assert.That(message, Does.Contain("Name: \"Red Maple\" → \"Red Maple Seedling\""));
        Assert.That(message, Does.Contain("Price: \"12.5\" → \"15.75\""));
        Assert.That(message, Does.Contain("Published: \"True\" → \"False\""));
        Assert.That(message, Does.Not.Contain("Sku:"));
    }

    [Test]
    public async Task Unchanged_skipped_and_invalid_fields_are_not_reported_as_updated()
    {
        var context = Context(Product(), Mapping("Name", "Red Maple"), Mapping("Price", "not a price"), Mapping("Sku", "NEW", skipped: true));
        await Synchronizer().SynchronizeAsync(context);
        Assert.That(context.Result.Messages.Any(message => message.StartsWith("Updated core product fields")), Is.False);
    }

    [Test]
    public async Task Standalone_name_override_is_logged_instead_of_the_superseded_mapping()
    {
        var context = Context(Product(), Mapping("Name", "Ordinary mapped name"));
        context.StandaloneProductNameOverride = "Seedling override name";
        await Synchronizer().SynchronizeAsync(context);
        var message = context.Result.Messages.Single(message => message.StartsWith("Updated core product fields"));
        Assert.That(message, Does.Contain("Name: \"Red Maple\" → \"Seedling override name\""));
        Assert.That(message, Does.Not.Contain("Ordinary mapped name"));
    }

    [Test]
    public async Task Clearing_a_field_is_reported_and_long_descriptions_are_explicitly_abbreviated()
    {
        var product = Product();
        product.ShortDescription = "Previous description";
        product.FullDescription = new string('a', 300);
        var context = Context(product, Mapping("ShortDescription", ""), Mapping("FullDescription", new string('b', 400)));
        await Synchronizer().SynchronizeAsync(context);
        var message = context.Result.Messages.Single(message => message.StartsWith("Updated core product fields"));
        Assert.That(message, Does.Contain("ShortDescription: \"Previous description\" → \"\""));
        Assert.That(message, Does.Contain("truncated; 300 characters").And.Contain("truncated; 400 characters"));
        Assert.That(message.Length, Is.LessThan(800));
    }

    [Test]
    public async Task Unicode_quotes_and_line_breaks_are_readable_and_unambiguous()
    {
        var context = Context(Product(), Mapping("Name", "Café \"Seedling\"\nPack"));
        await Synchronizer().SynchronizeAsync(context);
        var message = context.Result.Messages.Single(message => message.StartsWith("Updated core product fields"));
        Assert.That(message, Does.Contain("Café"));
        Assert.That(message, Does.Contain("\\\"Seedling\\\"").And.Contain("\\nPack"));
        Assert.That(message, Does.Not.Contain("\n"));
    }

    [Test]
    public async Task Lifecycle_purchasing_restoration_is_logged()
    {
        var product = Product();
        product.DisableBuyButton = true;
        var context = new AkeneoProductSyncContext
        {
            Source = Source(), Request = new AkeneoProductImportRequest(), Result = new AkeneoProductImportResult(),
            ExistingProduct = product,
            ExistingSyncState = new AkeneoProductSyncState { LifecycleStatusId = (int)AkeneoProductLifecycleStatus.PurchasingDisabled }
        };
        await Synchronizer().SynchronizeAsync(context);
        Assert.That(context.Result.Messages.Single(message => message.StartsWith("Updated core product fields")),
            Does.Contain("DisableBuyButton: \"True\" → \"False\""));
    }

    [Test]
    public void Failed_persistence_does_not_claim_the_fields_were_updated()
    {
        var context = Context(Product(), Mapping("Name", "New name"));
        var repository = new Mock<IRepository<Product>>();
        repository.Setup(repository => repository.UpdateAsync(It.IsAny<Product>(), false)).ThrowsAsync(new InvalidOperationException("Save failed"));
        var synchronizer = new AkeneoProductCoreSynchronizer(Mock.Of<IProductService>(), repository.Object);
        Assert.ThrowsAsync<InvalidOperationException>(() => synchronizer.SynchronizeAsync(context));
        Assert.That(context.Result.Messages.Any(message => message.StartsWith("Updated core product fields")), Is.False);
    }

    [Test]
    public async Task A_profile_that_disallows_updates_does_not_log_changes()
    {
        var context = Context(Product(), Mapping("Name", "New name"));
        context.Request.UpdateExistingProducts = false;
        await Synchronizer().SynchronizeAsync(context);
        Assert.That(context.ExistingProduct.Name, Is.EqualTo("Red Maple"));
        Assert.That(context.Result.Messages.Any(message => message.StartsWith("Updated core product fields")), Is.False);
    }

    private static AkeneoProductCoreSynchronizer Synchronizer() => new(Mock.Of<IProductService>(), Mock.Of<IRepository<Product>>());
    private static Product Product() => new() { Id = 1197, Name = "Red Maple", Sku = "SKU", Price = 12.5m, Published = true };
    private static AkeneoProductDefinition Source() => new() { Identifier = "SKU", Enabled = true, Values = JsonSerializer.SerializeToElement(new { }) };
    private static AkeneoProductSyncContext Context(Product product, params AkeneoResolvedMappedValue[] mappings) => new()
    {
        Source = Source(), SourceEntityType = AkeneoEntityType.Product, Request = new AkeneoProductImportRequest(),
        Result = new AkeneoProductImportResult(), ExistingProduct = product, MappedValues = mappings, Sku = "SKU", ProductKey = "SKU"
    };
    private static AkeneoResolvedMappedValue Mapping(string field, string value, bool skipped = false) => new()
    {
        Mapping = new AkeneoAttributeMapping { NopTargetTypeId = (int)NopTargetType.ProductField, NopTargetKey = field, AkeneoAttributeCode = field.ToLowerInvariant() },
        HasValue = true, Value = new AkeneoResolvedProductValue { DisplayValue = value },
        EvaluationOutcome = skipped ? AkeneoMappingEvaluationOutcome.SkippedByCondition : AkeneoMappingEvaluationOutcome.Applied
    };
}
