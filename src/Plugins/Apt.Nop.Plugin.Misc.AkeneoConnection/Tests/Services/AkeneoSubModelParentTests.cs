using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Seo;
using Nop.Data;
using Nop.Services.Catalog;
using Nop.Services.Seo;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

/// <summary>
/// A sub-model parent rule makes the matched sub-model (for example the
/// seedling root form) the nopCommerce parent, with its variants as
/// associated-to-product values and its own Sub-model parent mappings.
/// </summary>
[TestFixture]
public class AkeneoSubModelParentTests
{
    private const string SubModelCode = "red_maple_seedling";

    #region Hierarchy

    [Test]
    public void The_matched_sub_model_becomes_the_parent_with_its_ancestors_values()
    {
        var root = Model("red_maple", parent: null, ("species_name", "Red Maple"));
        var subModel = Model(SubModelCode, parent: "red_maple", ("root_variant_url_slug", "red-maple-seedling"));
        var leaf = new AkeneoProductDefinition
        {
            Identifier = "RM-SEED-25", Parent = SubModelCode,
            Values = Values(("pack_size", "25"))
        };

        var resolution = new AkeneoProductModelHierarchyResolver()
            .ResolveSubModelParent(leaf, [subModel, root]);

        Assert.Multiple(() =>
        {
            Assert.That(resolution.IsSubModelParent, Is.True);
            Assert.That(resolution.ParentMappingScope, Is.EqualTo(AkeneoAttributeMappingEntityScope.SubModelParent));
            Assert.That(resolution.EffectiveParentProductModel.Code, Is.EqualTo(SubModelCode));
            Assert.That(resolution.EffectiveParentProductModel.Values.TryGetProperty("species_name", out _), Is.True,
                "the parent inherits the root's values");
            Assert.That(resolution.EffectiveLeaf.Values.TryGetProperty("root_variant_url_slug", out _), Is.True,
                "the child keeps the inherited values it had as a standalone product");
        });
    }

    [Test]
    public void A_family_resolution_keeps_product_model_parent_mappings()
    {
        var root = Model("red_maple", parent: null);
        var subModel = Model(SubModelCode, parent: "red_maple");
        var leaf = new AkeneoProductDefinition { Identifier = "RM-1", Parent = SubModelCode, Values = Values() };

        var resolution = new AkeneoProductModelHierarchyResolver()
            .Resolve(leaf, [subModel, root], AkeneoProductModelHierarchyMode.RootProductModel);

        Assert.Multiple(() =>
        {
            Assert.That(resolution.IsSubModelParent, Is.False);
            Assert.That(resolution.ParentMappingScope, Is.EqualTo(AkeneoAttributeMappingEntityScope.ProductModel));
            Assert.That(resolution.EffectiveParentProductModel.Code, Is.EqualTo("red_maple"));
        });
    }

    #endregion

    #region Mapping role

    [Test]
    public void Mappings_saved_for_the_three_older_roles_do_not_apply_to_sub_model_parents()
    {
        var older = new AkeneoAttributeMapping { EntityScopeId = 7 };
        var optedIn = new AkeneoAttributeMapping
        {
            EntityScopeId = (int)(AkeneoAttributeMappingEntityScope.StandaloneProduct | AkeneoAttributeMappingEntityScope.SubModelParent)
        };

        Assert.Multiple(() =>
        {
            Assert.That(AkeneoAttributeMappingScopeHelper.AppliesTo(older, AkeneoAttributeMappingEntityScope.SubModelParent), Is.False);
            Assert.That(AkeneoAttributeMappingScopeHelper.AppliesTo(optedIn, AkeneoAttributeMappingEntityScope.SubModelParent), Is.True);
            Assert.That(AkeneoAttributeMappingScopeHelper.IsValidConfiguredScopeId(15), Is.True);
            Assert.That(AkeneoAttributeMappingScopeHelper.IsValidConfiguredScopeId(16), Is.False);
            Assert.That(AkeneoAttributeMappingScopeHelper.NormalizeCurrentScope(
                AkeneoAttributeMappingEntityScope.SubModelParent, new AkeneoProductDefinition(), AkeneoEntityType.ProductModel),
                Is.EqualTo(AkeneoAttributeMappingEntityScope.SubModelParent));
        });
    }

    #endregion

    #region Associated values

    [Test]
    public void The_rule_replaces_the_familys_attribute_name_template_and_visibility()
    {
        var context = new AkeneoVariantImportContext
        {
            Options = new AkeneoVariantRelationshipOptions
            {
                AssociatedProductAttributeId = 3,
                AssociatedValueNameTemplate = "{axes}",
                HideChildProductsWhenRepresentedByParent = true
            },
            SubModelParentRule = Rule(attributeId: 9, template: "{pack_size} seedlings", hide: false)
        };

        AkeneoVariantRelationshipService.ApplySubModelParentRule(context, AkeneoVariantRelationshipSource.AkeneoFamilyConfiguration);

        Assert.Multiple(() =>
        {
            Assert.That(context.Options.AssociatedProductAttributeId, Is.EqualTo(9));
            Assert.That(context.Options.AssociatedValueNameTemplate, Is.EqualTo("{pack_size} seedlings"));
            Assert.That(context.Options.HideChildProductsWhenRepresentedByParent, Is.False);
        });
    }

    [Test]
    public void An_existing_parent_keeps_its_associated_attribute_and_blank_settings_fall_back_to_the_family()
    {
        var context = new AkeneoVariantImportContext
        {
            Options = new AkeneoVariantRelationshipOptions { AssociatedProductAttributeId = 3, AssociatedValueNameTemplate = "{axes}" },
            SubModelParentRule = Rule(attributeId: 9, template: " ", hide: true)
        };

        AkeneoVariantRelationshipService.ApplySubModelParentRule(context, AkeneoVariantRelationshipSource.ExistingNopParent);

        Assert.Multiple(() =>
        {
            Assert.That(context.Options.AssociatedProductAttributeId, Is.EqualTo(3));
            Assert.That(context.Options.AssociatedValueNameTemplate, Is.EqualTo("{axes}"));
        });
    }

    #endregion

    #region Slug handover

    private Mock<IProductService> _products = null!;
    private Mock<IUrlRecordService> _urls = null!;
    private List<UrlRecord> _urlRecords = null!;
    private List<AkeneoProductSyncState> _states = null!;

    [SetUp]
    public void SetUp()
    {
        _products = new Mock<IProductService>();
        _urls = new Mock<IUrlRecordService>();
        _urlRecords = new List<UrlRecord>();
        _states = new List<AkeneoProductSyncState>();

        _urls.Setup(s => s.GetSeNameAsync("red-maple-seedling", It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync("red-maple-seedling");
        _urls.Setup(s => s.ValidateSeNameAsync(It.Is<Product>(p => p.Id == 10), "red-maple-seedling", It.IsAny<string>(), true))
            .ReturnsAsync("red-maple-seedling-2");
    }

    [Test]
    public async Task A_standalone_variants_slug_moves_to_its_new_sub_model_parent()
    {
        var held = SlugHeldByLiveProduct(5);
        _states.Add(State(nopProductId: 5, parentCode: SubModelCode));
        _urls.Setup(s => s.ValidateSeNameAsync(It.Is<Product>(p => p.Id == 5), It.Is<string>(v => v == null), "Live 5", true))
            .ReturnsAsync("live-5");

        var context = ParentContext();
        await Synchronizer().SynchronizeAsync(context);

        _urls.Verify(s => s.DeleteUrlRecordsAsync(It.Is<IList<UrlRecord>>(records => records.Single() == held)), Times.Once);
        _urls.Verify(s => s.SaveSlugAsync(context.Product, "red-maple-seedling", 0), Times.Once);
        _urls.Verify(s => s.SaveSlugAsync(It.Is<Product>(p => p.Id == 5), "live-5", 0), Times.Once);
        Assert.Multiple(() =>
        {
            Assert.That(context.Result.Success, Is.True);
            Assert.That(context.Result.Messages, Has.Some.Contains("a variant of this product model"));
        });
    }

    [Test]
    public async Task A_slug_held_by_a_variant_of_another_model_still_fails_the_parent()
    {
        var held = SlugHeldByLiveProduct(5);
        _states.Add(State(nopProductId: 5, parentCode: "red_maple_potted"));
        _urls.Setup(s => s.GetBySlugAsync("red-maple-seedling")).ReturnsAsync(held);

        var context = ParentContext();
        await Synchronizer().SynchronizeAsync(context);

        _urls.Verify(s => s.DeleteUrlRecordsAsync(It.IsAny<IList<UrlRecord>>()), Times.Never);
        Assert.That(context.Result.Success, Is.False);
    }

    [Test]
    public async Task A_variant_product_never_takes_a_slug_from_a_live_sibling()
    {
        var held = SlugHeldByLiveProduct(5);
        _states.Add(State(nopProductId: 5, parentCode: SubModelCode));
        _urls.Setup(s => s.GetBySlugAsync("red-maple-seedling")).ReturnsAsync(held);

        var context = ParentContext(AkeneoEntityType.Product);
        await Synchronizer().SynchronizeAsync(context);

        _urls.Verify(s => s.DeleteUrlRecordsAsync(It.IsAny<IList<UrlRecord>>()), Times.Never);
        Assert.That(context.Result.Success, Is.False);
    }

    private UrlRecord SlugHeldByLiveProduct(int productId)
    {
        var record = new UrlRecord { Id = 1, EntityId = productId, EntityName = nameof(Product), Slug = "red-maple-seedling", IsActive = true };
        _urlRecords.Add(record);
        _products.Setup(s => s.GetProductByIdAsync(productId))
            .ReturnsAsync(new Product { Id = productId, Name = $"Live {productId}" });
        return record;
    }

    private static AkeneoProductSyncState State(int nopProductId, string parentCode) => new()
    {
        SyncProfileId = 7,
        AkeneoEntityTypeId = (int)AkeneoEntityType.Product,
        AkeneoCode = "RM-SEED-25",
        AkeneoParentCode = parentCode,
        DestinationKindId = (int)AkeneoProductDestinationKind.NopProduct,
        NopProductId = nopProductId
    };

    private AkeneoProductSeoSynchronizer Synchronizer()
    {
        var urlRecordRepository = new Mock<IRepository<UrlRecord>>();
        urlRecordRepository.Setup(r => r.Table).Returns(() => _urlRecords.AsQueryable());
        var stateRepository = new Mock<IRepository<AkeneoProductSyncState>>();
        stateRepository.Setup(r => r.Table).Returns(() => _states.AsQueryable());

        return new AkeneoProductSeoSynchronizer(
            _products.Object,
            Mock.Of<IRepository<Product>>(),
            _urls.Object,
            urlRecordRepository: urlRecordRepository.Object,
            syncStateRepository: stateRepository.Object);
    }

    private static AkeneoProductSyncContext ParentContext(AkeneoEntityType entityType = AkeneoEntityType.ProductModel)
    {
        var source = Model(SubModelCode, parent: "red_maple");
        var parent = new Product { Id = 10, Name = "Red Maple Seedling" };

        return new AkeneoProductSyncContext
        {
            Source = source,
            SourceEntityType = entityType,
            Request = new AkeneoProductImportRequest
            {
                SyncProfileId = 7,
                SyncRunRecordId = 20,
                SeoFieldMissingValueBehavior = AkeneoMissingValueBehavior.ClearExisting
            },
            Result = new AkeneoProductImportResult(),
            MappedValues =
            [
                new AkeneoResolvedMappedValue
                {
                    Mapping = new AkeneoAttributeMapping
                    {
                        AkeneoAttributeCode = "root_variant_url_slug",
                        NopTargetTypeId = (int)NopTargetType.SeoField,
                        NopTargetKey = "SeName"
                    },
                    HasValue = true,
                    Value = new AkeneoResolvedProductValue
                    {
                        AttributeCode = "root_variant_url_slug",
                        DisplayValue = "red-maple-seedling",
                        RawData = JsonSerializer.SerializeToElement("red-maple-seedling")
                    },
                    ResolvedSourceDisplayName = "root_variant_url_slug"
                }
            ],
            SourceCode = SubModelCode,
            MappingFamilyCode = source.Family,
            MappingEntityScope = AkeneoAttributeMappingEntityScope.SubModelParent,
            ProductKey = SubModelCode,
            Sku = SubModelCode,
            ExistingProduct = parent,
            Product = parent
        };
    }

    #endregion

    private static AkeneoFamilySubModelRule Rule(int? attributeId, string template, bool hide) => new()
    {
        AkeneoAxisAttributeCode = "root_variant",
        TriggerValue = "seedling",
        ParentLevel = AkeneoSubModelParentLevel.MatchingSubModel,
        AssociatedProductAttributeId = attributeId,
        AssociatedValueNameTemplate = template,
        HideChildProducts = hide
    };

    private static AkeneoProductDefinition Model(string code, string? parent, params (string Code, string Value)[] values) => new()
    {
        Code = code,
        Parent = parent,
        Family = "trees",
        FamilyVariant = "trees_by_root_variant",
        Values = Values(values)
    };

    private static JsonElement Values(params (string Code, string Value)[] values) =>
        JsonSerializer.SerializeToElement(values.ToDictionary(
            value => value.Code,
            value => new[] { new { locale = (string?)null, scope = (string?)null, data = value.Value } }));
}
