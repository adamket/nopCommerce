using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services.DryRun;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Assets;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Media;
using Nop.Services.Seo;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services.DryRun;

[TestFixture]
public class AkeneoDryRunSectionParityTests
{
    [Test]
    public void Every_product_section_synchronizer_has_a_matching_dry_run_planner_order()
    {
        var assembly = typeof(IAkeneoProductSectionSynchronizer).Assembly;
        var synchronizerOrders = assembly.GetTypes()
            .Where(type => !type.IsAbstract &&
                           typeof(IAkeneoProductSectionSynchronizer).IsAssignableFrom(type))
            .Select(type => ((IAkeneoProductSectionSynchronizer)
                System.Runtime.CompilerServices.RuntimeHelpers
                    .GetUninitializedObject(type)).Order)
            .OrderBy(order => order)
            .ToArray();
        var plannerOrders = assembly.GetTypes()
            .Where(type => !type.IsAbstract &&
                           typeof(IAkeneoDryRunSectionPlanner).IsAssignableFrom(type))
            .Select(type => ((IAkeneoDryRunSectionPlanner)
                System.Runtime.CompilerServices.RuntimeHelpers
                    .GetUninitializedObject(type)).Order)
            .ToHashSet();

        Assert.That(
            synchronizerOrders.Where(order => !plannerOrders.Contains(order)),
            Is.Empty,
            "A new product section synchronizer must add a matching Dry Run planner at the same order.");
    }

    [Test]
    public void Every_product_section_synchronizer_owns_a_read_only_plan_provider()
    {
        var assembly = typeof(IAkeneoProductSectionSynchronizer).Assembly;
        var missingProviders = assembly.GetTypes()
            .Where(type => !type.IsAbstract &&
                           typeof(IAkeneoProductSectionSynchronizer).IsAssignableFrom(type))
            .Where(type => !typeof(IAkeneoSectionDryRunPlanProvider).IsAssignableFrom(type))
            .Select(type => type.FullName)
            .ToArray();

        Assert.That(
            missingProviders,
            Is.Empty,
            "Every write synchronizer must own the matching read-only Dry Run plan.");
    }


    [TestCase(typeof(AkeneoProductCoreSynchronizer))]
    [TestCase(typeof(AkeneoProductSeoSynchronizer))]
    [TestCase(typeof(AkeneoProductCategorySynchronizer))]
    [TestCase(typeof(AkeneoProductSpecificationSynchronizer))]
    [TestCase(typeof(AkeneoProductAttributeSynchronizer))]
    [TestCase(typeof(AkeneoProductCustomPropertySynchronizer))]
    public void Plan_driven_synchronizer_executes_its_shared_plan(Type synchronizerType)
    {
        var synchronizeMethod = synchronizerType.GetMethod(
            nameof(IAkeneoProductSectionSynchronizer.SynchronizeAsync));
        var buildPlanMethod = synchronizerType.GetMethod(
            "BuildPlanAsync",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.NonPublic);

        Assert.Multiple(() =>
        {
            Assert.That(synchronizeMethod, Is.Not.Null);
            Assert.That(buildPlanMethod, Is.Not.Null);
        });

        var asyncStateMachine = synchronizeMethod!
            .GetCustomAttributes(typeof(System.Runtime.CompilerServices.AsyncStateMachineAttribute), false)
            .Cast<System.Runtime.CompilerServices.AsyncStateMachineAttribute>()
            .SingleOrDefault();
        var methodWithBody = asyncStateMachine?.StateMachineType.GetMethod(
            "MoveNext",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic) ?? synchronizeMethod;
        var il = methodWithBody.GetMethodBody()?.GetILAsByteArray() ?? [];
        var planToken = BitConverter.GetBytes(buildPlanMethod!.MetadataToken);

        Assert.That(
            ContainsSequence(il, planToken),
            Is.True,
            $"{synchronizerType.Name}.SynchronizeAsync must call its shared BuildPlanAsync method so Dry Run and apply cannot drift.");
    }

    [Test]
    public async Task Core_planner_reports_the_field_written_by_core_synchronizer()
    {
        var productService = new Mock<IProductService>();
        productService
            .Setup(service => service.UpdateProductAsync(It.IsAny<Product>()))
            .Returns(Task.CompletedTask);

        var mapped = MappedValue(
            NopTargetType.ProductField,
            "Name",
            "New product name");
        var plannerContext = Context(
            new Product { Id = 10, Name = "Old product name", Sku = "SKU-1" },
            mapped);
        var writeContext = Context(
            new Product { Id = 10, Name = "Old product name", Sku = "SKU-1" },
            mapped);
        var model = new AkeneoProductMappingPreviewModel();

        var synchronizer = new AkeneoProductCoreSynchronizer(productService.Object);
        await new AkeneoDryRunProductCorePlanner(synchronizer)
            .PlanAsync(plannerContext, model);
        await synchronizer.SynchronizeAsync(writeContext);

        AssertPlannerMatchesWrite(model, writeContext, "Product fields", "Name", AkeneoDryRunOperationType.Update);
    }

    [Test]
    public async Task Seo_planner_reports_the_meta_field_written_by_seo_synchronizer()
    {
        var productService = new Mock<IProductService>();
        productService
            .Setup(service => service.UpdateProductAsync(It.IsAny<Product>()))
            .Returns(Task.CompletedTask);
        var urlRecordService = new Mock<IUrlRecordService>();

        var mapped = MappedValue(
            NopTargetType.SeoField,
            "MetaTitle",
            "New meta title");
        var plannerContext = Context(
            new Product { Id = 10, Name = "Product", MetaTitle = "Old meta title" },
            mapped);
        var writeContext = Context(
            new Product { Id = 10, Name = "Product", MetaTitle = "Old meta title" },
            mapped);
        var model = new AkeneoProductMappingPreviewModel();

        var synchronizer = new AkeneoProductSeoSynchronizer(
            productService.Object,
            urlRecordService.Object);
        await new AkeneoDryRunSeoPlanner(synchronizer)
            .PlanAsync(plannerContext, model);
        await synchronizer.SynchronizeAsync(writeContext);

        AssertPlannerMatchesWrite(model, writeContext, "SEO fields", "MetaTitle", AkeneoDryRunOperationType.Update);
    }

    [Test]
    public async Task Category_planner_reports_the_assignment_created_by_category_synchronizer()
    {
        var categoryService = new Mock<ICategoryService>();
        var entityMappingService = new Mock<IAkeneoNopEntityMappingService>();
        var managedRelationService = new Mock<IAkeneoManagedRelationService>();

        entityMappingService
            .Setup(service => service.GetMappedNopEntityIdByAkeneoCodeAsync(
                AkeneoEntityType.Category,
                "shade",
                NopEntityType.Category))
            .ReturnsAsync(100);
        categoryService
            .Setup(service => service.GetCategoryByIdAsync(100))
            .ReturnsAsync(new Category { Id = 100, Name = "Shade Trees" });
        categoryService
            .Setup(service => service.GetProductCategoriesByProductIdAsync(10, true))
            .ReturnsAsync(new List<ProductCategory>());
        categoryService
            .Setup(service => service.InsertProductCategoryAsync(It.IsAny<ProductCategory>()))
            .Callback<ProductCategory>(item => item.Id = 501)
            .Returns(Task.CompletedTask);
        managedRelationService
            .Setup(service => service.UpsertAsync(
                7,
                20,
                10,
                AkeneoManagedRelationType.ProductCategory,
                501,
                null,
                "100"))
            .ReturnsAsync(new AkeneoManagedRelation());

        var plannerContext = Context(new Product { Id = 10, Name = "Product" });
        plannerContext.Source.Categories = ["shade"];
        plannerContext.Request.CategorySyncMode = AkeneoCollectionSyncMode.Merge;
        var writeContext = Context(new Product { Id = 10, Name = "Product" });
        writeContext.Source.Categories = ["shade"];
        writeContext.Request.CategorySyncMode = AkeneoCollectionSyncMode.Merge;
        var model = new AkeneoProductMappingPreviewModel();

        var synchronizer = new AkeneoProductCategorySynchronizer(
            categoryService.Object,
            entityMappingService.Object,
            managedRelationService.Object);
        var planner = new AkeneoDryRunCategoryPlanner(synchronizer);

        await planner.PlanAsync(plannerContext, model);
        await synchronizer.SynchronizeAsync(writeContext);

        AssertPlannerMatchesWrite(model, writeContext, "Categories", "Shade Trees", AkeneoDryRunOperationType.Add);
    }

    [Test]
    public async Task Specification_planner_reports_the_assignment_created_by_specification_synchronizer()
    {
        var specificationService = new Mock<ISpecificationAttributeService>();
        var entityMappingService = new Mock<IAkeneoNopEntityMappingService>();
        var managedRelationService = new Mock<IAkeneoManagedRelationService>();

        var definition = new SpecificationAttribute { Id = 5, Name = "Color" };
        var option = new SpecificationAttributeOption
        {
            Id = 50,
            SpecificationAttributeId = 5,
            Name = "Red"
        };
        specificationService.SetReturnsDefault(
            Task.FromResult<IList<SpecificationAttribute>>([definition]));
        specificationService.SetReturnsDefault(
            Task.FromResult<IList<SpecificationAttributeOption>>([option]));
        specificationService.SetReturnsDefault(
            Task.FromResult<IList<ProductSpecificationAttribute>>([]));
        specificationService.SetReturnsDefault(
            Task.FromResult<SpecificationAttributeOption>(option));
        specificationService
            .Setup(service => service.InsertProductSpecificationAttributeAsync(
                It.IsAny<ProductSpecificationAttribute>()))
            .Callback<ProductSpecificationAttribute>(item => item.Id = 600)
            .Returns(Task.CompletedTask);
        entityMappingService
            .Setup(service => service.GetMappedNopEntityIdByAkeneoCodeAsync(
                AkeneoEntityType.Option,
                It.IsAny<string>(),
                NopEntityType.SpecificationAttributeOption))
            .ReturnsAsync(50);
        managedRelationService
            .Setup(service => service.UpsertAsync(
                7,
                20,
                10,
                AkeneoManagedRelationType.ProductSpecificationAssignment,
                600,
                It.IsAny<string>(),
                "red"))
            .ReturnsAsync(new AkeneoManagedRelation());

        var mapped = OptionMappedValue(
            NopTargetType.SpecificationAttribute,
            5,
            "color",
            "red",
            "Red");
        var plannerContext = Context(new Product { Id = 10, Name = "Product" }, mapped);
        var writeContext = Context(new Product { Id = 10, Name = "Product" }, mapped);
        var model = new AkeneoProductMappingPreviewModel();

        var synchronizer = new AkeneoProductSpecificationSynchronizer(
            specificationService.Object,
            entityMappingService.Object,
            managedRelationService.Object);
        var planner = new AkeneoDryRunSpecificationPlanner(synchronizer);

        await planner.PlanAsync(plannerContext, model);
        await synchronizer.SynchronizeAsync(writeContext);

        AssertPlannerMatchesWrite(model, writeContext, "Specification attributes", "Color: Red", AkeneoDryRunOperationType.Add);
    }


    [Test]
    public async Task Specification_plan_reuses_one_created_option_for_duplicate_labels()
    {
        var specificationService = new Mock<ISpecificationAttributeService>();
        var entityMappingService = new Mock<IAkeneoNopEntityMappingService>();
        var managedRelationService = new Mock<IAkeneoManagedRelationService>();

        specificationService.SetReturnsDefault(
            Task.FromResult<IList<SpecificationAttribute>>(
                [new SpecificationAttribute { Id = 5, Name = "Color" }]));
        specificationService.SetReturnsDefault(
            Task.FromResult<IList<SpecificationAttributeOption>>([]));
        specificationService.SetReturnsDefault(
            Task.FromResult<IList<ProductSpecificationAttribute>>([]));
        specificationService
            .Setup(service => service.InsertSpecificationAttributeOptionAsync(
                It.IsAny<SpecificationAttributeOption>()))
            .Callback<SpecificationAttributeOption>(item => item.Id = 50)
            .Returns(Task.CompletedTask);
        specificationService
            .Setup(service => service.InsertProductSpecificationAttributeAsync(
                It.IsAny<ProductSpecificationAttribute>()))
            .Callback<ProductSpecificationAttribute>(item => item.Id = 60)
            .Returns(Task.CompletedTask);
        entityMappingService.SetReturnsDefault(Task.FromResult<int?>(null));
        entityMappingService
            .Setup(service => service.UpsertAkeneoNopEntityMappingAsync(
                AkeneoEntityType.Option,
                It.IsAny<string>(),
                null,
                NopEntityType.SpecificationAttributeOption,
                50))
            .ReturnsAsync(true);
        managedRelationService.SetReturnsDefault(
            Task.FromResult(new AkeneoManagedRelation()));

        var mapped = MultiOptionMappedValue(
            NopTargetType.SpecificationAttribute,
            5,
            "color",
            ["red-primary", "red-secondary"],
            ["Red", "Red"]);
        var context = Context(new Product { Id = 10, Name = "Product" }, mapped);
        var synchronizer = new AkeneoProductSpecificationSynchronizer(
            specificationService.Object,
            entityMappingService.Object,
            managedRelationService.Object);

        await synchronizer.SynchronizeAsync(context);

        specificationService.Verify(
            service => service.InsertSpecificationAttributeOptionAsync(
                It.IsAny<SpecificationAttributeOption>()),
            Times.Once);
        specificationService.Verify(
            service => service.InsertProductSpecificationAttributeAsync(
                It.IsAny<ProductSpecificationAttribute>()),
            Times.Once);
        entityMappingService.Verify(
            service => service.UpsertAkeneoNopEntityMappingAsync(
                AkeneoEntityType.Option,
                It.IsAny<string>(),
                null,
                NopEntityType.SpecificationAttributeOption,
                50),
            Times.Exactly(2));
    }

    [Test]
    public async Task Product_attribute_planner_reports_mapping_and_value_created_by_synchronizer()
    {
        var productAttributeService = new Mock<IProductAttributeService>();
        var entityMappingService = new Mock<IAkeneoNopEntityMappingService>();
        var managedRelationService = new Mock<IAkeneoManagedRelationService>();
        var familyMappingService = new Mock<IAkeneoFamilyMappingService>();

        var definition = new ProductAttribute { Id = 5, Name = "Size" };
        productAttributeService.SetReturnsDefault(
            Task.FromResult<IList<ProductAttribute>>([definition]));
        productAttributeService.SetReturnsDefault(
            Task.FromResult<IList<ProductAttributeMapping>>([]));
        productAttributeService.SetReturnsDefault(
            Task.FromResult<IList<ProductAttributeValue>>([]));
        productAttributeService.SetReturnsDefault(
            Task.FromResult<IList<ProductAttributeCombination>>([]));
        familyMappingService.SetReturnsDefault(
            Task.FromResult<AkeneoFamilyMapping>(null!));
        productAttributeService
            .Setup(service => service.InsertProductAttributeMappingAsync(
                It.IsAny<ProductAttributeMapping>()))
            .Callback<ProductAttributeMapping>(item => item.Id = 40)
            .Returns(Task.CompletedTask);
        productAttributeService
            .Setup(service => service.InsertProductAttributeValueAsync(
                It.IsAny<ProductAttributeValue>()))
            .Callback<ProductAttributeValue>(item => item.Id = 50)
            .Returns(Task.CompletedTask);
        entityMappingService
            .Setup(service => service.GetMappedNopEntityIdByAkeneoCodeAsync(
                AkeneoEntityType.Option,
                It.IsAny<string>(),
                NopEntityType.ProductAttributeValue))
            .ReturnsAsync((int?)null);
        entityMappingService
            .Setup(service => service.UpsertAkeneoNopEntityMappingAsync(
                AkeneoEntityType.Option,
                It.IsAny<string>(),
                null,
                NopEntityType.ProductAttributeValue,
                50))
            .ReturnsAsync(true);
        managedRelationService
            .Setup(service => service.UpsertAsync(
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<AkeneoManagedRelationType>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .ReturnsAsync(new AkeneoManagedRelation());

        var mapped = OptionMappedValue(
            NopTargetType.ProductAttribute,
            5,
            "size",
            "large",
            "Large");
        var plannerContext = Context(new Product { Id = 10, Name = "Product" }, mapped);
        var writeContext = Context(new Product { Id = 10, Name = "Product" }, mapped);
        var model = new AkeneoProductMappingPreviewModel();

        var synchronizer = new AkeneoProductAttributeSynchronizer(
            productAttributeService.Object,
            entityMappingService.Object,
            managedRelationService.Object,
            familyMappingService.Object);
        var planner = new AkeneoDryRunProductAttributePlanner(synchronizer);

        await planner.PlanAsync(plannerContext, model);
        await synchronizer.SynchronizeAsync(writeContext);

        AssertPlannerMatchesWrite(model, writeContext, "Product attributes", "Size", AkeneoDryRunOperationType.Add);
    }


    [Test]
    public async Task Product_attribute_plan_does_not_create_duplicate_values_for_duplicate_labels()
    {
        var productAttributeService = new Mock<IProductAttributeService>();
        var entityMappingService = new Mock<IAkeneoNopEntityMappingService>();
        var managedRelationService = new Mock<IAkeneoManagedRelationService>();
        var familyMappingService = new Mock<IAkeneoFamilyMappingService>();

        productAttributeService.SetReturnsDefault(
            Task.FromResult<IList<ProductAttribute>>(
                [new ProductAttribute { Id = 5, Name = "Size" }]));
        productAttributeService.SetReturnsDefault(
            Task.FromResult<IList<ProductAttributeMapping>>([]));
        productAttributeService.SetReturnsDefault(
            Task.FromResult<IList<ProductAttributeValue>>([]));
        productAttributeService.SetReturnsDefault(
            Task.FromResult<IList<ProductAttributeCombination>>([]));
        familyMappingService.SetReturnsDefault(
            Task.FromResult<AkeneoFamilyMapping>(null!));
        productAttributeService
            .Setup(service => service.InsertProductAttributeMappingAsync(
                It.IsAny<ProductAttributeMapping>()))
            .Callback<ProductAttributeMapping>(item => item.Id = 40)
            .Returns(Task.CompletedTask);
        productAttributeService
            .Setup(service => service.InsertProductAttributeValueAsync(
                It.IsAny<ProductAttributeValue>()))
            .Callback<ProductAttributeValue>(item => item.Id = 50)
            .Returns(Task.CompletedTask);
        entityMappingService
            .Setup(service => service.UpsertAkeneoNopEntityMappingAsync(
                AkeneoEntityType.Option,
                It.IsAny<string>(),
                null,
                NopEntityType.ProductAttributeValue,
                50))
            .ReturnsAsync(true);
        managedRelationService.SetReturnsDefault(
            Task.FromResult(new AkeneoManagedRelation()));

        var mapped = MultiOptionMappedValue(
            NopTargetType.ProductAttribute,
            5,
            "size",
            ["large-primary", "large-secondary"],
            ["Large", "Large"]);
        var context = Context(new Product { Id = 10, Name = "Product" }, mapped);
        var synchronizer = new AkeneoProductAttributeSynchronizer(
            productAttributeService.Object,
            entityMappingService.Object,
            managedRelationService.Object,
            familyMappingService.Object);

        await synchronizer.SynchronizeAsync(context);

        productAttributeService.Verify(
            service => service.InsertProductAttributeMappingAsync(
                It.IsAny<ProductAttributeMapping>()),
            Times.Once);
        productAttributeService.Verify(
            service => service.InsertProductAttributeValueAsync(
                It.IsAny<ProductAttributeValue>()),
            Times.Once);
    }

    [Test]
    public async Task Custom_property_planner_reports_the_value_written_by_synchronizer()
    {
        var genericAttributeService = new Mock<IGenericAttributeService>();
        genericAttributeService.SetReturnsDefault(Task.FromResult<string>("old"));

        var mapped = MappedValue(
            NopTargetType.CustomProperty,
            "Apt.Test.Value",
            "new");
        var plannerContext = Context(new Product { Id = 10, Name = "Product" }, mapped);
        var writeContext = Context(new Product { Id = 10, Name = "Product" }, mapped);
        var model = new AkeneoProductMappingPreviewModel();

        var synchronizer =
            new AkeneoProductCustomPropertySynchronizer(genericAttributeService.Object);
        await new AkeneoDryRunCustomPropertyPlanner(synchronizer)
            .PlanAsync(plannerContext, model);
        await synchronizer.SynchronizeAsync(writeContext);

        AssertPlannerMatchesWrite(model, writeContext, "Custom properties", "Apt.Test.Value", AkeneoDryRunOperationType.Update);
    }

    [Test]
    public async Task Core_executable_action_does_not_depend_on_preview_labels()
    {
        var productService = new Mock<IProductService>();
        productService
            .Setup(service => service.UpdateProductAsync(It.IsAny<Product>()))
            .Returns(Task.CompletedTask);

        var mapped = MappedValue(
            NopTargetType.ProductField,
            "Name",
            "New product name");
        var context = Context(
            new Product { Id = 10, Name = "Old product name", Sku = "SKU-1" },
            mapped);
        var synchronizer = new AkeneoProductCoreSynchronizer(productService.Object);

        var plan = await InvokeBuildPlanAsync(
            synchronizer,
            context,
            context.ExistingProduct,
            false,
            CancellationToken.None);

        RenamePreviewLabels(plan);
        await ExecutePlanAsync(plan, context);

        Assert.That(context.Product?.Name, Is.EqualTo("New product name"));
    }

    [Test]
    public async Task Seo_executable_action_does_not_depend_on_preview_labels()
    {
        var productService = new Mock<IProductService>();
        productService
            .Setup(service => service.UpdateProductAsync(It.IsAny<Product>()))
            .Returns(Task.CompletedTask);
        var urlRecordService = new Mock<IUrlRecordService>();

        var mapped = MappedValue(
            NopTargetType.SeoField,
            "MetaTitle",
            "New meta title");
        var context = Context(
            new Product { Id = 10, Name = "Product", MetaTitle = "Old meta title" },
            mapped);
        var synchronizer = new AkeneoProductSeoSynchronizer(
            productService.Object,
            urlRecordService.Object);

        var plan = await InvokeBuildPlanAsync(
            synchronizer,
            context,
            null,
            CancellationToken.None);

        RenamePreviewLabels(plan);
        await ExecutePlanAsync(plan, context);

        Assert.That(context.Product?.MetaTitle, Is.EqualTo("New meta title"));
    }

    [Test]
    public async Task Custom_property_executable_action_does_not_depend_on_preview_labels()
    {
        var genericAttributeService = new Mock<IGenericAttributeService>();
        genericAttributeService.SetReturnsDefault(Task.FromResult<string>("old"));

        var mapped = MappedValue(
            NopTargetType.CustomProperty,
            "Apt.Test.Value",
            "new");
        var context = Context(new Product { Id = 10, Name = "Product" }, mapped);
        var synchronizer =
            new AkeneoProductCustomPropertySynchronizer(genericAttributeService.Object);

        var plan = await InvokeBuildPlanAsync(
            synchronizer,
            context,
            CancellationToken.None);

        RenamePreviewLabels(plan);
        await ExecutePlanAsync(plan, context);

        genericAttributeService.Verify(service => service.SaveAttributeAsync<string>(
            context.Product,
            "Apt.Test.Value",
            "new",
            It.IsAny<int>()), Times.Once);
    }

    [Test]
    public async Task Seo_slug_preview_uses_typed_core_name_after_core_labels_are_renamed()
    {
        var productService = new Mock<IProductService>();
        var urlRecordService = new Mock<IUrlRecordService>();
        string validatedName = null;

        urlRecordService
            .Setup(service => service.ValidateSeNameAsync(
                It.IsAny<Product>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                true))
            .Callback<Product, string, string, bool>((_, _, name, _) =>
                validatedName = name)
            .ReturnsAsync("new-product-name");

        var context = Context(
            null,
            MappedValue(
                NopTargetType.ProductField,
                "Name",
                "New product name"));
        var model = new AkeneoProductMappingPreviewModel();
        var core = new AkeneoProductCoreSynchronizer(productService.Object);
        var seo = new AkeneoProductSeoSynchronizer(
            productService.Object,
            urlRecordService.Object);

        await new AkeneoDryRunProductCorePlanner(core)
            .PlanAsync(context, model);

        foreach (var operation in model.Operations)
        {
            operation.Area = "Renamed core area";
            operation.Target = "Renamed core target";
        }

        await new AkeneoDryRunSeoPlanner(seo)
            .PlanAsync(context, model);

        Assert.Multiple(() =>
        {
            Assert.That(
                context.PlanningState.ProposedProductName,
                Is.EqualTo("New product name"));
            Assert.That(validatedName, Is.EqualTo("New product name"));
            Assert.That(model.Operations, Has.Some.Matches<AkeneoDryRunOperationPreviewModel>(
                operation => operation.Area == "SEO fields" &&
                             operation.Target == "SeName" &&
                             operation.ProposedValue == "new-product-name"));
        });
    }

    [Test]
    public async Task Asset_planner_and_synchronizer_share_resolver_and_payload_rules()
    {
        var mappingService = new Mock<IAkeneoAssetMappingService>();
        var managedAssetService = new Mock<IAkeneoManagedAssetService>();
        var resolver = new Mock<IAkeneoAssetResolver>();
        var apiClient = new Mock<IAkeneoApiClient>();
        var downloader = new Mock<IAkeneoExternalAssetDownloader>();
        var pictureService = new Mock<IPictureService>();
        var videoService = new Mock<IVideoService>();
        var productService = new Mock<IProductService>();
        var genericAttributeService = new Mock<IGenericAttributeService>();

        var mapping = new AkeneoAssetMapping
        {
            Id = 1,
            MappingKey = "documents",
            Name = "Documents",
            Enabled = true,
            SourceAttributeCode = "documents",
            DestinationTypeId = (int)AkeneoAssetDestinationType.CustomProperty,
            StorageModeId = (int)AkeneoAssetStorageMode.ExternalReference,
            EntityScopeId = (int)AkeneoAttributeMappingEntityScope.All,
            CustomPropertyKey = "Apt.Documents"
        };
        var asset = new AkeneoResolvedAsset
        {
            Mapping = mapping,
            SourceIdentityHash = "asset-1",
            SourceFingerprint = "fingerprint-1",
            AssetCode = "care-guide",
            ExternalUrl = "https://example.test/care-guide.pdf",
            OriginalFileName = "care-guide.pdf",
            DisplayOrder = 1
        };

        mappingService
            .Setup(service => service.GetEffectiveMappingsAsync("family"))
            .ReturnsAsync([mapping]);
        resolver
            .Setup(service => service.ResolveAsync(
                It.IsAny<AkeneoProductSyncContext>(),
                mapping,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AkeneoAssetResolutionResult
            {
                CanReconcile = true,
                Assets = [asset]
            });
        managedAssetService.SetReturnsDefault(
            Task.FromResult<IList<AkeneoManagedAsset>>([]));
        managedAssetService
            .Setup(service => service.InsertAsync(It.IsAny<AkeneoManagedAsset>()))
            .Returns(Task.CompletedTask);
        genericAttributeService.SetReturnsDefault(Task.FromResult<string>(null!));
        string savedPayload = null;
        genericAttributeService
            .Setup(service => service.SaveAttributeAsync<string>(
                It.IsAny<Product>(),
                "Apt.Documents",
                It.IsAny<string>(),
                It.IsAny<int>()))
            .Callback<Product, string, string, int>((_, _, value, _) =>
                savedPayload = value)
            .Returns(Task.CompletedTask);

        var synchronizer = new AkeneoProductAssetSynchronizer(
            mappingService.Object,
            managedAssetService.Object,
            resolver.Object,
            apiClient.Object,
            downloader.Object,
            pictureService.Object,
            videoService.Object,
            productService.Object,
            genericAttributeService.Object);
        var planner = new AkeneoDryRunAssetPlanner(synchronizer);
        var plannerContext = Context(new Product { Id = 10, Name = "Product" });
        plannerContext.Request.AssetSyncMode = AkeneoCollectionSyncMode.Merge;
        var writeContext = Context(new Product { Id = 10, Name = "Product" });
        writeContext.Request.AssetSyncMode = AkeneoCollectionSyncMode.Merge;
        var model = new AkeneoProductMappingPreviewModel();

        await planner.PlanAsync(plannerContext, model);
        await synchronizer.SynchronizeAsync(writeContext);

        AssertPlannerMatchesWrite(model, writeContext, "Assets", "Apt.Documents", AkeneoDryRunOperationType.Update);

        var plannedPayload = model.Operations
            .Single(operation =>
                operation.Area == "Assets" &&
                operation.Target.Contains("Apt.Documents", StringComparison.OrdinalIgnoreCase))
            .ProposedValue;

        var expectedPreviewPayload = savedPayload?.Length <= 240
            ? savedPayload
            : savedPayload?[..240] + "…";

        Assert.That(plannedPayload, Is.EqualTo(expectedPreviewPayload),
            "The structural asset payload shown by Dry Run must summarize the exact payload saved by the write path.");
        resolver.Verify(service => service.ResolveAsync(
            It.IsAny<AkeneoProductSyncContext>(),
            mapping,
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Test]
    public async Task Asset_replace_all_resolution_failure_preserves_existing_media_in_plan_and_write()
    {
        var mappingService = new Mock<IAkeneoAssetMappingService>();
        var managedAssetService = new Mock<IAkeneoManagedAssetService>();
        var resolver = new Mock<IAkeneoAssetResolver>();
        var apiClient = new Mock<IAkeneoApiClient>();
        var downloader = new Mock<IAkeneoExternalAssetDownloader>();
        var pictureService = new Mock<IPictureService>();
        var videoService = new Mock<IVideoService>();
        var productService = new Mock<IProductService>();
        var genericAttributeService = new Mock<IGenericAttributeService>();

        var mapping = new AkeneoAssetMapping
        {
            Id = 2,
            MappingKey = "pictures",
            Name = "Pictures",
            Enabled = true,
            SourceAttributeCode = "pictures",
            DestinationTypeId = (int)AkeneoAssetDestinationType.ProductPicture,
            StorageModeId = (int)AkeneoAssetStorageMode.ImportIntoNopCommerce,
            EntityScopeId = (int)AkeneoAttributeMappingEntityScope.All
        };

        mappingService
            .Setup(service => service.GetEffectiveMappingsAsync("family"))
            .ReturnsAsync([mapping]);
        resolver
            .Setup(service => service.ResolveAsync(
                It.IsAny<AkeneoProductSyncContext>(),
                mapping,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AkeneoAssetResolutionResult
            {
                CanReconcile = false,
                Warning = "Temporary asset lookup failure"
            });
        managedAssetService.SetReturnsDefault(
            Task.FromResult<IList<AkeneoManagedAsset>>(
            [
                new AkeneoManagedAsset
                {
                    Id = 10,
                    NopProductId = 10,
                    AssetMappingKey = "deleted-mapping",
                    DestinationTypeId = (int)AkeneoAssetDestinationType.ProductPicture,
                    SourceIdentityHash = "old-picture",
                    NopPictureId = 100,
                    NopProductPictureId = 101
                }
            ]));

        var synchronizer = new AkeneoProductAssetSynchronizer(
            mappingService.Object,
            managedAssetService.Object,
            resolver.Object,
            apiClient.Object,
            downloader.Object,
            pictureService.Object,
            videoService.Object,
            productService.Object,
            genericAttributeService.Object);
        var planner = new AkeneoDryRunAssetPlanner(synchronizer);
        var plannerContext = Context(new Product { Id = 10, Name = "Product" });
        plannerContext.Request.AssetSyncMode = AkeneoCollectionSyncMode.ReplaceAll;
        var writeContext = Context(new Product { Id = 10, Name = "Product" });
        writeContext.Request.AssetSyncMode = AkeneoCollectionSyncMode.ReplaceAll;
        var model = new AkeneoProductMappingPreviewModel();

        await planner.PlanAsync(plannerContext, model);
        await synchronizer.SynchronizeAsync(writeContext);

        Assert.Multiple(() =>
        {
            Assert.That(writeContext.HasChanges, Is.False);
            Assert.That(model.Operations, Has.None.Matches<AkeneoDryRunOperationPreviewModel>(
                operation => operation.ChangeType == AkeneoDryRunOperationType.Remove));
            Assert.That(model.Operations, Has.Some.Matches<AkeneoDryRunOperationPreviewModel>(
                operation => operation.Area == "Assets" &&
                             operation.ChangeType == AkeneoDryRunOperationType.Review));
        });
        managedAssetService.Verify(
            service => service.DeleteAsync(It.IsAny<AkeneoManagedAsset>()),
            Times.Never);
    }

    [Test]
    public async Task Hierarchy_planner_surfaces_parent_and_representation_decision()
    {
        var model = new AkeneoProductMappingPreviewModel
        {
            HierarchyDecision =
                "Akeneo leaf 'SKU-1' uses root-product-model flattening and will materialize as a product attribute combination.",
            ParentProductDecision =
                "Parent model 'red_maple' would be created before the leaf is applied.",
            ImmediateParentProductModelCode = "red_maple_bare_root",
            CurrentRepresentation = "No existing nopCommerce parent structure",
            HierarchyRequiresReview = true
        };

        await new AkeneoDryRunHierarchyPlanner()
            .PlanAsync(Context(new Product { Id = 10 }), model);

        Assert.That(model.Operations, Has.Some.Matches<AkeneoDryRunOperationPreviewModel>(
            operation => operation.Area == "Hierarchy and representation" &&
                         operation.ChangeType == AkeneoDryRunOperationType.Review &&
                         operation.ProposedValue.Contains("product attribute combination")));
    }


    private static bool ContainsSequence(
        IReadOnlyList<byte> source,
        IReadOnlyList<byte> sequence)
    {
        if (sequence.Count == 0 || source.Count < sequence.Count)
            return false;

        for (var index = 0; index <= source.Count - sequence.Count; index++)
        {
            var matches = true;

            for (var offset = 0; offset < sequence.Count; offset++)
            {
                if (source[index + offset] == sequence[offset])
                    continue;

                matches = false;
                break;
            }

            if (matches)
                return true;
        }

        return false;
    }

    private static void AssertPlannerMatchesWrite(
        AkeneoProductMappingPreviewModel model,
        AkeneoProductSyncContext writeContext,
        string area,
        string target,
        AkeneoDryRunOperationType expectedChangeType)
    {
        Assert.Multiple(() =>
        {
            Assert.That(writeContext.HasChanges, Is.True,
                "The representative real synchronizer scenario should perform a write.");
            Assert.That(model.Operations, Has.Some.Matches<AkeneoDryRunOperationPreviewModel>(
                operation => operation.WillChange &&
                             operation.Area.Equals(area, StringComparison.OrdinalIgnoreCase) &&
                             operation.Target.Contains(target, StringComparison.OrdinalIgnoreCase) &&
                             operation.ChangeType == expectedChangeType),
                "The matching Dry Run planner must report the write as a planned change.");
        });
    }

    private static async Task<object> InvokeBuildPlanAsync(
        object synchronizer,
        params object[] arguments)
    {
        var method = synchronizer.GetType().GetMethod(
            "BuildPlanAsync",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.NonPublic);

        Assert.That(method, Is.Not.Null);

        var task = (Task)method!.Invoke(synchronizer, arguments)!;
        await task;

        var result = task.GetType().GetProperty("Result")?.GetValue(task);
        Assert.That(result, Is.Not.Null);
        return result!;
    }

    private static void RenamePreviewLabels(object plan)
    {
        var operations = (System.Collections.IEnumerable)plan.GetType()
            .GetProperty("Operations")!
            .GetValue(plan)!;

        foreach (var operation in operations)
        {
            var preview = operation!.GetType()
                .GetProperty("Preview")!
                .GetValue(operation);

            if (preview == null)
                continue;

            preview.GetType().GetProperty("Area")!.SetValue(preview, "Renamed UI area");
            preview.GetType().GetProperty("Target")!.SetValue(preview, "Renamed UI target");
        }
    }

    private static async Task ExecutePlanAsync(
        object plan,
        AkeneoProductSyncContext context)
    {
        var method = plan.GetType().GetMethod("ExecuteAsync");
        Assert.That(method, Is.Not.Null);

        var task = (Task)method!.Invoke(
            plan,
            [context, CancellationToken.None])!;
        await task;
    }

    private static AkeneoProductSyncContext Context(
        Product product,
        params AkeneoResolvedMappedValue[] mappedValues)
    {
        var source = new AkeneoProductDefinition
        {
            Uuid = "uuid-1",
            Identifier = "SKU-1",
            Family = "family",
            Enabled = true,
            Values = JsonSerializer.SerializeToElement(new Dictionary<string, object>())
        };

        return new AkeneoProductSyncContext
        {
            Source = source,
            SourceEntityType = AkeneoEntityType.Product,
            Request = new AkeneoProductImportRequest
            {
                SyncProfileId = 7,
                SyncRunRecordId = 20,
                CreateNewProducts = true,
                UpdateExistingProducts = true,
                ProductFieldMissingValueBehavior = AkeneoMissingValueBehavior.ClearExisting,
                SeoFieldMissingValueBehavior = AkeneoMissingValueBehavior.ClearExisting,
                CustomPropertyMissingValueBehavior = AkeneoMissingValueBehavior.ClearExisting,
                CategorySyncMode = AkeneoCollectionSyncMode.Merge,
                SpecificationAttributeSyncMode = AkeneoCollectionSyncMode.Merge,
                ProductAttributeSyncMode = AkeneoCollectionSyncMode.Merge,
                AssetSyncMode = AkeneoCollectionSyncMode.Merge,
                CreateMissingSpecificationAttributeOptions = true,
                CreateMissingProductAttributeValues = true
            },
            Result = new AkeneoProductImportResult(),
            MappedValues = mappedValues,
            SourceCode = source.Identifier,
            SourceUuid = source.Uuid,
            MappingFamilyCode = source.Family,
            MappingEntityScope = AkeneoAttributeMappingEntityScope.StandaloneProduct,
            ProductKey = source.Identifier,
            Sku = source.Identifier,
            ExistingProduct = product,
            Product = product
        };
    }

    private static AkeneoResolvedMappedValue MappedValue(
        NopTargetType targetType,
        string targetKey,
        string displayValue,
        int? targetEntityId = null,
        string sourceCode = "source")
    {
        return new AkeneoResolvedMappedValue
        {
            Mapping = new AkeneoAttributeMapping
            {
                AkeneoAttributeCode = sourceCode,
                NopTargetTypeId = (int)targetType,
                NopTargetKey = targetKey,
                NopTargetEntityId = targetEntityId
            },
            HasValue = true,
            Value = new AkeneoResolvedProductValue
            {
                AttributeCode = sourceCode,
                DisplayValue = displayValue,
                RawData = JsonSerializer.SerializeToElement(displayValue)
            },
            ResolvedSourceDisplayName = sourceCode
        };
    }


    private static AkeneoResolvedMappedValue MultiOptionMappedValue(
        NopTargetType targetType,
        int targetEntityId,
        string sourceCode,
        IReadOnlyList<string> optionCodes,
        IReadOnlyList<string> optionLabels)
    {
        return new AkeneoResolvedMappedValue
        {
            Mapping = new AkeneoAttributeMapping
            {
                AkeneoAttributeCode = sourceCode,
                NopTargetTypeId = (int)targetType,
                NopTargetEntityId = targetEntityId
            },
            HasValue = true,
            Value = new AkeneoResolvedProductValue
            {
                AttributeCode = sourceCode,
                DisplayValue = string.Join(", ", optionLabels),
                DisplayValues = optionLabels.ToList(),
                RawData = JsonSerializer.SerializeToElement(optionCodes)
            },
            ResolvedSourceDisplayName = sourceCode
        };
    }

    private static AkeneoResolvedMappedValue OptionMappedValue(
        NopTargetType targetType,
        int targetEntityId,
        string sourceCode,
        string optionCode,
        string optionLabel)
    {
        return new AkeneoResolvedMappedValue
        {
            Mapping = new AkeneoAttributeMapping
            {
                AkeneoAttributeCode = sourceCode,
                NopTargetTypeId = (int)targetType,
                NopTargetEntityId = targetEntityId
            },
            HasValue = true,
            Value = new AkeneoResolvedProductValue
            {
                AttributeCode = sourceCode,
                DisplayValue = optionLabel,
                DisplayValues = [optionLabel],
                RawData = JsonSerializer.SerializeToElement(new[] { optionCode })
            },
            ResolvedSourceDisplayName = sourceCode
        };
    }
}
