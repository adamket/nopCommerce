using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Factories;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

[TestFixture]
public class AkeneoProductBatchImportRequestFactoryTests
{
    private AkeneoProductBatchImportRequestFactory _factory = null!;

    [SetUp]
    public void SetUp() =>
        _factory = new AkeneoProductBatchImportRequestFactory(new AkeneoProductSearchJsonBuilder());

    [Test]
    public void CreateFromProfile_maps_profile_execution_and_write_policies()
    {
        var profile = CreateProfile();
        profile.ProductWriteModeId = (int)AkeneoProductWriteMode.UpdateOnly;
        profile.PageSize = 0;
        profile.AkeneoLocales = "es_ES,en_US";
        profile.CurrencyCode = " CAD ";
        profile.CategorySyncModeId = (int)AkeneoCollectionSyncMode.ReplaceManaged;
        profile.SpecificationAttributeSyncModeId = (int)AkeneoCollectionSyncMode.Merge;
        profile.ProductAttributeSyncModeId = (int)AkeneoCollectionSyncMode.ReplaceAll;
        profile.AssetSyncModeId = (int)AkeneoCollectionSyncMode.Disabled;

        var request = _factory.CreateFromProfile(profile, syncRunRecordId: 19);

        Assert.Multiple(() =>
        {
            Assert.That(request.SyncProfileId, Is.EqualTo(profile.Id));
            Assert.That(request.SyncRunRecordId, Is.EqualTo(19));
            Assert.That(request.Locale, Is.EqualTo("es_ES"));
            Assert.That(request.Currency, Is.EqualTo("CAD"));
            Assert.That(request.PageSize, Is.EqualTo(100));
            Assert.That(request.CreateNewProducts, Is.False);
            Assert.That(request.UpdateExistingProducts, Is.True);
            Assert.That(request.CategorySyncMode, Is.EqualTo(AkeneoCollectionSyncMode.ReplaceManaged));
            Assert.That(request.ProductAttributeSyncMode, Is.EqualTo(AkeneoCollectionSyncMode.ReplaceAll));
            Assert.That(request.AssetSyncMode, Is.EqualTo(AkeneoCollectionSyncMode.Disabled));
            Assert.That(request.ScopeHash, Is.Not.Empty);
        });
    }

    [Test]
    public void CreateFromProfile_since_last_successful_run_uses_two_minute_overlap()
    {
        var profile = CreateProfile();
        profile.UpdatedFilterModeId = (int)AkeneoUpdatedFilterMode.SinceLastSuccessfulRun;
        var watermark = new DateTime(2026, 7, 30, 10, 0, 0, DateTimeKind.Utc);

        var request = _factory.CreateFromProfile(profile, 1, watermark);

        Assert.That(request.UpdatedAfterUtc, Is.EqualTo(watermark.AddMinutes(-2)));
    }

    [Test]
    public void CreateFromProfile_fixed_date_uses_configured_cutoff()
    {
        var profile = CreateProfile();
        profile.UpdatedFilterModeId = (int)AkeneoUpdatedFilterMode.FixedDate;
        profile.UpdatedAfterUtc = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);

        var request = _factory.CreateFromProfile(profile, 1);

        Assert.That(request.UpdatedAfterUtc, Is.EqualTo(profile.UpdatedAfterUtc));
    }

    [Test]
    public void CreateFromProfile_rolling_days_uses_current_utc_window()
    {
        var profile = CreateProfile();
        profile.UpdatedFilterModeId = (int)AkeneoUpdatedFilterMode.RollingDays;
        profile.UpdatedSinceLastNDays = 3;
        var earliestExpected = DateTime.UtcNow.AddDays(-3).AddSeconds(-2);

        var request = _factory.CreateFromProfile(profile, 1);

        var latestExpected = DateTime.UtcNow.AddDays(-3).AddSeconds(2);
        Assert.Multiple(() =>
        {
            Assert.That(request.UpdatedAfterUtc.HasValue, Is.True);
            Assert.That(request.UpdatedAfterUtc!.Value, Is.InRange(earliestExpected, latestExpected));
        });
    }

    [Test]
    public void CreateFromProfile_none_does_not_add_updated_filter()
    {
        var profile = CreateProfile();
        profile.UpdatedFilterModeId = (int)AkeneoUpdatedFilterMode.None;
        profile.UpdatedAfterUtc = DateTime.UtcNow.AddYears(-1);

        var request = _factory.CreateFromProfile(profile, 1);

        Assert.Multiple(() =>
        {
            Assert.That(request.UpdatedAfterUtc, Is.Null);
            Assert.That(request.SearchJson ?? string.Empty, Does.Not.Contain("updated"));
        });
    }

    [Test]
    public void CreateFromProfile_full_run_ignores_updated_mode_and_linked_asset_filter()
    {
        var profile = CreateProfile();
        profile.UpdatedFilterModeId = (int)AkeneoUpdatedFilterMode.FixedDate;
        profile.UpdatedAfterUtc = DateTime.UtcNow.AddDays(-30);
        profile.IncludeLinkedAssetUpdates = true;

        var request = _factory.CreateFromProfile(
            profile,
            1,
            runMode: AkeneoRunMode.Full);

        Assert.Multiple(() =>
        {
            Assert.That(request.UpdatedAfterUtc, Is.Null);
            Assert.That(request.IncludeLinkedAssetUpdates, Is.False);
            Assert.That(request.SearchJson ?? string.Empty, Does.Not.Contain("updated_including_linked_entities"));
        });
    }

    [Test]
    public void CreateFromProfile_generates_expected_search_filters()
    {
        var profile = CreateProfile();
        profile.AkeneoFamilyCodes = "trees,shrubs";
        profile.AkeneoCategoryCodes = "shade";
        profile.CategoryFilterModeId = (int)AkeneoCategoryFilterMode.InChildren;
        profile.ProductEnabledFilterId = (int)AkeneoProductEnabledFilter.EnabledOnly;
        profile.ProductParentFilterModeId = (int)AkeneoProductParentFilterMode.VariantProductsOnly;

        var request = _factory.CreateFromProfile(profile, 1);
        using var document = JsonDocument.Parse(request.SearchJson!);

        Assert.Multiple(() =>
        {
            Assert.That(document.RootElement.GetProperty("family")[0]
                .GetProperty("value").GetArrayLength(), Is.EqualTo(2));
            Assert.That(document.RootElement.GetProperty("categories")[0]
                .GetProperty("operator").GetString(), Is.EqualTo("IN CHILDREN"));
            Assert.That(document.RootElement.GetProperty("enabled")[0]
                .GetProperty("value").GetBoolean(), Is.True);
            Assert.That(document.RootElement.GetProperty("parent")[0]
                .GetProperty("operator").GetString(), Is.EqualTo("NOT EMPTY"));
        });
    }

    private static AkeneoSyncProfile CreateProfile() => new()
    {
        Id = 7,
        Name = "Default",
        Enabled = true,
        AkeneoChannel = "ecommerce",
        AkeneoLocales = "en_US",
        CurrencyCode = "USD",
        ProductWriteModeId = (int)AkeneoProductWriteMode.CreateAndUpdate,
        UnmappedAttributeBehaviorId = (int)UnmappedAkeneoAttributeBehavior.Ignore,
        ProductFieldMissingValueBehaviorId = (int)AkeneoMissingValueBehavior.PreserveExisting,
        SeoFieldMissingValueBehaviorId = (int)AkeneoMissingValueBehavior.PreserveExisting,
        CustomPropertyMissingValueBehaviorId = (int)AkeneoMissingValueBehavior.PreserveExisting,
        CategorySyncModeId = (int)AkeneoCollectionSyncMode.Merge,
        SpecificationAttributeSyncModeId = (int)AkeneoCollectionSyncMode.Merge,
        ProductAttributeSyncModeId = (int)AkeneoCollectionSyncMode.Merge,
        AssetSyncModeId = (int)AkeneoCollectionSyncMode.ReplaceManaged,
        MissingProductBehaviorId = (int)AkeneoMissingProductBehavior.Ignore,
        PageSize = 100,
        ContinueOnError = true,
        AkeneoFamilyCodes = string.Empty,
        AkeneoCategoryCodes = string.Empty,
        AkeneoProductGroupCodes = string.Empty,
        CategoryFilterModeId = (int)AkeneoCategoryFilterMode.None,
        ProductEnabledFilterId = (int)AkeneoProductEnabledFilter.Any,
        ProductParentFilterModeId = (int)AkeneoProductParentFilterMode.Any,
        UpdatedFilterModeId = (int)AkeneoUpdatedFilterMode.SinceLastSuccessfulRun
    };
}
