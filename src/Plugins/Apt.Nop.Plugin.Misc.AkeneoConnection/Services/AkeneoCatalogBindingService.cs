using System.Text.Json;
using System.Transactions;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Binding;
using Humanizer;
using Nop.Core.Domain.Catalog;
using Nop.Data;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

/// <summary>
/// Binds an existing nopCommerce catalog to Akeneo without touching any
/// nopCommerce entity. Variants are matched by SKU; each product model's
/// nopCommerce parent is then inferred from where its matched variants live
/// (combination owner, associated-product owner or grouped parent). Only the
/// plugin's global entity-mapping table is written.
/// </summary>
public class AkeneoCatalogBindingService(
    IAkeneoApiClient akeneoApiClient,
    IAkeneoProductValueResolver productValueResolver,
    IAkeneoNopEntityMappingService entityMappingService,
    IAkeneoFamilyMappingService familyMappingService,
    IAkeneoProductModelHierarchyResolver hierarchyResolver,
    IAkeneoSyncLeaseService syncLeaseService,
    IAkeneoSyncRunRecordService syncRunRecordService,
    IProductAttributeParser productAttributeParser,
    IRepository<Product> productRepository,
    IRepository<ProductAttributeCombination> combinationRepository,
    IRepository<ProductAttributeMapping> productAttributeMappingRepository,
    IRepository<ProductAttributeValue> productAttributeValueRepository)
    : IAkeneoCatalogBindingService
{
    private const int PageSize = 100;
    private const int QueryChunkSize = 500;
    private const int MaxListedItems = 500;
    private const int MaxAncestorDepth = 10;
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromHours(1);

    #region Plan types

    private enum LeafKind
    {
        Combination,
        Product
    }

    private sealed record LeafCandidate(
        LeafKind Kind,
        int TargetId,
        int? ParentProductId,
        string Representation);

    private sealed class LeafPlan
    {
        public AkeneoProductDefinition Source { get; init; }

        public string Identifier { get; init; }

        public string Uuid { get; init; }

        public string FamilyCode { get; init; }

        /// <summary>Product model bound to the nopCommerce parent; null for standalone.</summary>
        public string BindingModelCode { get; set; }

        public AkeneoFamilyMapping FamilyConfiguration { get; set; }

        public AkeneoProductDefinition EffectiveLeaf { get; set; }

        public List<LeafCandidate> Candidates { get; } = new();

        public bool ExistingBinding { get; set; }

        public LeafCandidate Selected { get; set; }

        public AkeneoBindingStatus Status { get; set; } = AkeneoBindingStatus.Unmatched;
    }

    private sealed class ModelPlan
    {
        public string Code { get; init; }

        public string FamilyCode { get; init; }

        public List<LeafPlan> Leaves { get; } = new();

        public Dictionary<int, int> Votes { get; } = new();

        public Dictionary<int, int> Ambiguous { get; } = new();

        public int? ParentProductId { get; set; }

        public bool ResolvedByChoice { get; set; }

        public AkeneoBindingStatus Status { get; set; } = AkeneoBindingStatus.Unmatched;
    }

    private sealed class AxisPlan
    {
        public string MappingCode { get; init; }

        public int ValueId { get; init; }

        public AkeneoBindingStatus Status { get; set; }
    }

    private sealed class BindingPlan
    {
        public List<string> FamilyCodes { get; init; } = new();

        public List<LeafPlan> Leaves { get; } = new();

        public Dictionary<string, ModelPlan> Models { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, AxisPlan> AxisValues { get; } = new(StringComparer.Ordinal);

        public HashSet<string> ConflictingAxisCodes { get; } = new(StringComparer.Ordinal);

        public List<AkeneoBindingIssue> Issues { get; } = new();

        public Dictionary<int, Product> ProductsById { get; } = new();

        public Dictionary<int, ProductAttributeCombination> CombinationsById { get; } = new();

        public HashSet<int> ExistingBoundProductIds { get; } = new();

        public void AddIssue(string level, string code, string familyCode, string message) =>
            Issues.Add(new AkeneoBindingIssue
            {
                Level = level,
                AkeneoCode = code,
                FamilyCode = familyCode,
                Message = message
            });
    }

    #endregion

    #region Public API

    public async Task<AkeneoCatalogBindingReport> ScanAsync(
        IList<string> familyCodes,
        CancellationToken cancellationToken = default)
    {
        var plan = await BuildPlanAsync(familyCodes, null, cancellationToken);
        var report = await BuildReportAsync(plan, cancellationToken);

        report.Message = report.Conflicts.Count == 0
            ? "Scan complete. Commit will write every matched binding."
            : $"Scan complete. {report.Conflicts.Count} conflict(s) need a parent choice; " +
              "unresolved conflicts are left unbound on commit.";

        return report;
    }

    public async Task<AkeneoCatalogBindingReport> CommitAsync(
        IList<string> familyCodes,
        IDictionary<string, int> conflictChoices,
        CancellationToken cancellationToken = default)
    {
        var lease = await syncLeaseService.TryAcquireAsync(
            AkeneoConnectionConstants.CatalogWriterLockKey,
            LeaseDuration);

        if (lease == null)
        {
            return new AkeneoCatalogBindingReport
            {
                Success = false,
                AlreadyRunning = true,
                Message = "An Akeneo sync or binding run is already in progress. Try again when it finishes."
            };
        }

        var startedOnUtc = DateTime.UtcNow;
        var runRecord = new AkeneoSyncRunRecord
        {
            SyncProfileId = null,
            SyncTypeId = (int)SyncType.CatalogBinding,
            RunModeId = (int)AkeneoRunMode.Binding,
            SyncStatusId = (int)SyncStatus.Started,
            StartedOnUtc = startedOnUtc,
            WatermarkUtc = startedOnUtc
        };

        try
        {
            await syncRunRecordService.InsertAkeneoSyncRunRecordAsync(runRecord);
            await syncLeaseService.SetRunRecordAsync(lease, runRecord.Id);

            var plan = await BuildPlanAsync(
                familyCodes,
                conflictChoices ?? new Dictionary<string, int>(),
                cancellationToken);

            runRecord.SearchJsonSnapshot = BuildFamilySearchJson(plan.FamilyCodes)?.Truncate(4000);

            var written = await WritePlanAsync(plan, cancellationToken);

            // Bindings just written count as bound for the unbound-product report.
            var report = await BuildReportAsync(plan, cancellationToken);
            report.Committed = true;
            report.WrittenCount = written;
            report.SyncRunRecordId = runRecord.Id;
            report.Resolutions = BuildResolutions(plan);
            report.Message =
                $"Binding committed. {written} binding(s) written; " +
                $"{report.Conflicts.Count} conflict(s) left unbound.";

            runRecord.FinishedOnUtc = DateTime.UtcNow;
            runRecord.TotalRead = plan.Leaves.Count + plan.Models.Count;
            runRecord.CreatedCount = written;
            runRecord.SkippedCount = report.Conflicts.Count;
            runRecord.WarningCount = report.IssueTotal;
            runRecord.CompletedAllPages = true;
            runRecord.SyncStatusId = report.Conflicts.Count > 0 || report.IssueTotal > 0
                ? (int)SyncStatus.CompletedWithWarnings
                : (int)SyncStatus.Completed;
            runRecord.ErrorSummary = report.IssueTotal > 0
                ? string.Join(Environment.NewLine, plan.Issues
                        .Take(25)
                        .Select(issue => $"{issue.Level} {issue.AkeneoCode}: {issue.Message}"))
                    .Truncate(4000)
                : null;

            await syncRunRecordService.UpdateAkeneoSyncRunRecordAsync(runRecord);

            return report;
        }
        catch (OperationCanceledException)
        {
            runRecord.FinishedOnUtc = DateTime.UtcNow;
            runRecord.SyncStatusId = (int)SyncStatus.Cancelled;
            await syncRunRecordService.UpdateAkeneoSyncRunRecordAsync(runRecord);
            throw;
        }
        catch (Exception ex)
        {
            runRecord.FinishedOnUtc = DateTime.UtcNow;
            runRecord.SyncStatusId = (int)SyncStatus.Failed;
            runRecord.ErrorSummary = ex.Message.Truncate(4000);

            if (runRecord.Id > 0)
                await syncRunRecordService.UpdateAkeneoSyncRunRecordAsync(runRecord);

            return new AkeneoCatalogBindingReport
            {
                Success = false,
                SyncRunRecordId = runRecord.Id > 0 ? runRecord.Id : null,
                Message = "Catalog binding failed; nothing was written. " + ex.Message
            };
        }
        finally
        {
            await syncLeaseService.ReleaseAsync(lease);
        }
    }

    #endregion

    #region Plan

    private async Task<BindingPlan> BuildPlanAsync(
        IList<string> familyCodes,
        IDictionary<string, int> conflictChoices,
        CancellationToken cancellationToken)
    {
        var plan = new BindingPlan
        {
            FamilyCodes = (familyCodes ?? new List<string>())
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList()
        };

        var searchJson = BuildFamilySearchJson(plan.FamilyCodes);

        var productModels = (await ReadAllAsync(
                (searchAfter, token) => akeneoApiClient.GetProductModelsPageAsync(
                    PageSize, searchAfter, searchJson, token),
                cancellationToken))
            .Where(model => !string.IsNullOrWhiteSpace(model.Code))
            .GroupBy(model => model.Code.Trim(), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var products = await ReadAllAsync(
            (searchAfter, token) => akeneoApiClient.GetProductsPageAsync(
                PageSize, searchAfter, searchJson, token),
            cancellationToken);

        await ResolveLeafHierarchyAsync(plan, products, productModels, cancellationToken);

        var existing = await LoadExistingMappingsAsync(plan, cancellationToken);
        await ResolveLeafCandidatesAsync(plan, existing, cancellationToken);

        ResolveStandaloneLeaves(plan);
        ResolveModels(plan, existing.ModelParents, conflictChoices);
        await ResolveAxisValuesAsync(plan, existing.AxisValues, cancellationToken);

        return plan;
    }

    private async Task ResolveLeafHierarchyAsync(
        BindingPlan plan,
        IList<AkeneoProductDefinition> products,
        IDictionary<string, AkeneoProductDefinition> productModels,
        CancellationToken cancellationToken)
    {
        var configurationCache = new Dictionary<string, AkeneoFamilyMapping>(StringComparer.Ordinal);
        var ruleCache = new Dictionary<int, IList<AkeneoFamilySubModelRule>>();
        var familiesWithoutMapping = new HashSet<string>(StringComparer.Ordinal);

        foreach (var source in products)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var identifier = source.Identifier?.Trim();
            var familyCode = source.Family?.Trim();

            if (string.IsNullOrWhiteSpace(identifier))
            {
                plan.AddIssue("Variant", source.Uuid, familyCode,
                    "Akeneo product has no identifier, so it cannot be matched by SKU.");
                continue;
            }

            var leaf = new LeafPlan
            {
                Source = source,
                Identifier = identifier,
                Uuid = source.Uuid?.Trim(),
                FamilyCode = familyCode,
                EffectiveLeaf = source
            };

            if (string.IsNullOrWhiteSpace(source.Parent))
            {
                plan.Leaves.Add(leaf);
                continue;
            }

            var ancestors = BuildAncestors(source, productModels);

            if (ancestors == null)
            {
                plan.AddIssue("Variant", identifier, familyCode,
                    $"Parent product model '{source.Parent}' (or one of its ancestors) was not found in Akeneo.");
                continue;
            }

            var familyVariant = AkeneoLeafFamilyVariantResolver.Resolve(source, ancestors);

            if (!familyVariant.Success)
            {
                plan.AddIssue("Variant", identifier, familyCode, familyVariant.Error);
                continue;
            }

            var configurationKey = $"{familyCode}|{familyVariant.FamilyVariantCode}";

            if (!configurationCache.TryGetValue(configurationKey, out var configuration))
            {
                configuration = await familyMappingService.GetEffectiveMappingAsync(
                    familyCode,
                    familyVariant.FamilyVariantCode);
                configurationCache[configurationKey] = configuration;
            }

            if (configuration is not { Enabled: true })
            {
                configuration = null;

                if (familiesWithoutMapping.Add(configurationKey))
                {
                    plan.AddIssue("Family", familyVariant.FamilyVariantCode, familyCode,
                        "No enabled family mapping. Bindings are inferred using the immediate parent " +
                        "model, but the product sync will fail for these variants until a family mapping exists.");
                }
            }

            var hierarchy = hierarchyResolver.Resolve(
                source,
                ancestors,
                configuration?.ProductModelHierarchyMode ??
                AkeneoProductModelHierarchyMode.ImmediateParentProductModel);

            leaf.FamilyConfiguration = configuration;
            leaf.EffectiveLeaf = hierarchy.EffectiveLeaf;
            leaf.BindingModelCode = hierarchy.ParentProductModel.Code?.Trim();

            if (configuration != null)
            {
                if (!ruleCache.TryGetValue(configuration.Id, out var rules))
                {
                    rules = await familyMappingService.GetSubModelRulesAsync(configuration.Id);
                    ruleCache[configuration.Id] = rules;
                }

                var rule = AkeneoSubModelRuleMatcher.FindMatch(
                    rules,
                    source,
                    hierarchy.ImmediateParentModel);

                // A rule that imports this leaf as a standalone product.
                if (rule?.VariantRelationshipOverrideMode == AkeneoVariantRelationshipMode.None)
                {
                    leaf.BindingModelCode = null;
                    leaf.EffectiveLeaf = rule.MergeAncestorValues
                        ? hierarchy.LeafWithInheritedValues
                        : source;
                }
            }

            plan.Leaves.Add(leaf);
        }
    }

    private static List<AkeneoProductDefinition> BuildAncestors(
        AkeneoProductDefinition leaf,
        IDictionary<string, AkeneoProductDefinition> productModels)
    {
        var ancestors = new List<AkeneoProductDefinition>();
        var parentCode = leaf.Parent?.Trim();

        while (!string.IsNullOrWhiteSpace(parentCode))
        {
            if (ancestors.Count >= MaxAncestorDepth ||
                !productModels.TryGetValue(parentCode, out var model))
            {
                return null;
            }

            ancestors.Add(model);
            parentCode = model.Parent?.Trim();
        }

        return ancestors;
    }

    private sealed class ExistingMappings
    {
        public Dictionary<string, int> ModelParents { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, int> ProductsByUuid { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, int> ProductsByCode { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, int> CombinationsByUuid { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, int> CombinationsByCode { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, int> AxisValues { get; } = new(StringComparer.Ordinal);
    }

    private async Task<ExistingMappings> LoadExistingMappingsAsync(
        BindingPlan plan,
        CancellationToken cancellationToken)
    {
        var existing = new ExistingMappings();

        foreach (var mapping in await entityMappingService
                     .GetAkeneoNopEntityMappingsAsync(AkeneoEntityType.ProductModel))
        {
            if (mapping.NopEntityTypeId == (int)NopEntityType.Product &&
                !string.IsNullOrWhiteSpace(mapping.AkeneoCode))
            {
                existing.ModelParents[mapping.AkeneoCode.Trim()] = mapping.NopEntityId;
            }
        }

        foreach (var mapping in await entityMappingService
                     .GetAkeneoNopEntityMappingsAsync(AkeneoEntityType.Product))
        {
            var (byUuid, byCode) = mapping.NopEntityTypeId switch
            {
                (int)NopEntityType.Product =>
                    (existing.ProductsByUuid, existing.ProductsByCode),
                (int)NopEntityType.ProductAttributeCombination =>
                    (existing.CombinationsByUuid, existing.CombinationsByCode),
                _ => (null, null)
            };

            if (byUuid == null)
                continue;

            if (!string.IsNullOrWhiteSpace(mapping.AkeneoUuid))
                byUuid[mapping.AkeneoUuid.Trim()] = mapping.NopEntityId;

            if (!string.IsNullOrWhiteSpace(mapping.AkeneoCode))
                byCode[mapping.AkeneoCode.Trim()] = mapping.NopEntityId;
        }

        foreach (var mapping in await entityMappingService
                     .GetAkeneoNopEntityMappingsAsync(AkeneoEntityType.Option))
        {
            if (mapping.NopEntityTypeId == (int)NopEntityType.ProductAttributeValue &&
                mapping.AkeneoCode?.StartsWith("variant-axis:", StringComparison.Ordinal) == true)
            {
                existing.AxisValues[mapping.AkeneoCode] = mapping.NopEntityId;
            }
        }

        await LoadCombinationsByIdAsync(
            plan,
            existing.CombinationsByUuid.Values.Concat(existing.CombinationsByCode.Values),
            cancellationToken);

        await LoadProductsByIdAsync(
            plan,
            existing.ModelParents.Values
                .Concat(existing.ProductsByUuid.Values)
                .Concat(existing.ProductsByCode.Values)
                .Concat(plan.CombinationsById.Values.Select(combination => combination.ProductId)),
            cancellationToken);

        foreach (var id in existing.ModelParents.Values
                     .Concat(existing.ProductsByUuid.Values)
                     .Concat(existing.ProductsByCode.Values)
                     .Concat(plan.CombinationsById.Values.Select(combination => combination.ProductId)))
        {
            if (IsLiveProduct(plan, id))
                plan.ExistingBoundProductIds.Add(id);
        }

        return existing;
    }

    private async Task ResolveLeafCandidatesAsync(
        BindingPlan plan,
        ExistingMappings existing,
        CancellationToken cancellationToken)
    {
        var skus = plan.Leaves
            .Select(leaf => leaf.Identifier)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var combinationsBySku = new Dictionary<string, List<ProductAttributeCombination>>(
            StringComparer.OrdinalIgnoreCase);
        var productsBySku = new Dictionary<string, List<Product>>(StringComparer.OrdinalIgnoreCase);

        foreach (var chunk in skus.Chunk(QueryChunkSize))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var combinations = await combinationRepository.Table
                .Where(combination => chunk.Contains(combination.Sku))
                .ToListAsync();

            foreach (var combination in combinations)
            {
                plan.CombinationsById[combination.Id] = combination;
                AddToGroup(combinationsBySku, combination.Sku?.Trim(), combination);
            }

            var products = await productRepository.Table
                .Where(product => !product.Deleted && chunk.Contains(product.Sku))
                .ToListAsync();

            foreach (var product in products)
            {
                plan.ProductsById[product.Id] = product;
                AddToGroup(productsBySku, product.Sku?.Trim(), product);
            }
        }

        // Parents of matched combinations must exist and not be deleted.
        await LoadProductsByIdAsync(
            plan,
            plan.CombinationsById.Values.Select(combination => combination.ProductId),
            cancellationToken);

        var candidateProductIds = productsBySku.Values
            .SelectMany(group => group)
            .Select(product => product.Id)
            .Concat(existing.ProductsByUuid.Values)
            .Concat(existing.ProductsByCode.Values)
            .Distinct()
            .ToList();

        var associatedParents = await LoadAssociatedParentsAsync(candidateProductIds, cancellationToken);

        await LoadProductsByIdAsync(
            plan,
            associatedParents.Values.SelectMany(parents => parents)
                .Concat(plan.ProductsById.Values
                    .Where(product => product.ParentGroupedProductId > 0)
                    .Select(product => product.ParentGroupedProductId)),
            cancellationToken);

        foreach (var leaf in plan.Leaves)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var boundCombinationId = Lookup(existing.CombinationsByUuid, leaf.Uuid) ??
                                     Lookup(existing.CombinationsByCode, leaf.Identifier);

            if (boundCombinationId.HasValue &&
                plan.CombinationsById.TryGetValue(boundCombinationId.Value, out var boundCombination) &&
                IsLiveProduct(plan, boundCombination.ProductId))
            {
                leaf.ExistingBinding = true;
                leaf.Candidates.Add(new LeafCandidate(
                    LeafKind.Combination,
                    boundCombination.Id,
                    boundCombination.ProductId,
                    "combination"));
                continue;
            }

            var boundProductId = Lookup(existing.ProductsByUuid, leaf.Uuid) ??
                                 Lookup(existing.ProductsByCode, leaf.Identifier);

            if (boundProductId.HasValue && IsLiveProduct(plan, boundProductId.Value))
            {
                leaf.ExistingBinding = true;
                leaf.Candidates.AddRange(ClassifyProduct(
                    plan,
                    plan.ProductsById[boundProductId.Value],
                    associatedParents));
                continue;
            }

            if (combinationsBySku.TryGetValue(leaf.Identifier, out var combinationsForSku))
            {
                foreach (var combination in combinationsForSku)
                {
                    if (IsLiveProduct(plan, combination.ProductId))
                    {
                        leaf.Candidates.Add(new LeafCandidate(
                            LeafKind.Combination,
                            combination.Id,
                            combination.ProductId,
                            "combination"));
                    }
                }
            }

            if (productsBySku.TryGetValue(leaf.Identifier, out var productsForSku))
            {
                foreach (var product in productsForSku)
                    leaf.Candidates.AddRange(ClassifyProduct(plan, product, associatedParents));
            }
        }
    }

    private static IEnumerable<LeafCandidate> ClassifyProduct(
        BindingPlan plan,
        Product product,
        IDictionary<int, List<int>> associatedParents)
    {
        if (product.ParentGroupedProductId > 0 &&
            IsLiveProduct(plan, product.ParentGroupedProductId))
        {
            yield return new LeafCandidate(
                LeafKind.Product,
                product.Id,
                product.ParentGroupedProductId,
                "grouped child");
            yield break;
        }

        var parents = associatedParents.TryGetValue(product.Id, out var found)
            ? found.Where(parentId => IsLiveProduct(plan, parentId)).ToList()
            : new List<int>();

        if (parents.Count == 0)
        {
            yield return new LeafCandidate(LeafKind.Product, product.Id, null, "standalone");
            yield break;
        }

        foreach (var parentId in parents)
            yield return new LeafCandidate(LeafKind.Product, product.Id, parentId, "associated product");
    }

    private static void ResolveStandaloneLeaves(BindingPlan plan)
    {
        foreach (var leaf in plan.Leaves.Where(leaf => leaf.BindingModelCode == null))
        {
            var productTargets = leaf.Candidates
                .Where(candidate => candidate.Kind == LeafKind.Product)
                .GroupBy(candidate => candidate.TargetId)
                .Select(group => group.First())
                .ToList();

            if (productTargets.Count == 1)
            {
                leaf.Selected = productTargets[0];
                leaf.Status = leaf.ExistingBinding
                    ? AkeneoBindingStatus.AlreadyBound
                    : AkeneoBindingStatus.Matched;
                continue;
            }

            if (productTargets.Count > 1)
            {
                leaf.Status = AkeneoBindingStatus.Issue;
                plan.AddIssue("Variant", leaf.Identifier, leaf.FamilyCode,
                    $"SKU matches {productTargets.Count} nopCommerce products " +
                    $"({string.Join(", ", productTargets.Select(candidate => "#" + candidate.TargetId))}).");
                continue;
            }

            if (leaf.Candidates.Count > 0)
            {
                leaf.Status = AkeneoBindingStatus.Issue;
                plan.AddIssue("Variant", leaf.Identifier, leaf.FamilyCode,
                    "Akeneo imports this as a standalone product, but in nopCommerce the SKU belongs to an attribute combination.");
                continue;
            }

            leaf.Status = AkeneoBindingStatus.Unmatched;
        }
    }

    private static void ResolveModels(
        BindingPlan plan,
        IDictionary<string, int> existingModelParents,
        IDictionary<string, int> conflictChoices)
    {
        foreach (var leaf in plan.Leaves.Where(leaf => leaf.BindingModelCode != null))
        {
            if (!plan.Models.TryGetValue(leaf.BindingModelCode, out var model))
            {
                model = new ModelPlan { Code = leaf.BindingModelCode, FamilyCode = leaf.FamilyCode };
                plan.Models[model.Code] = model;
            }

            model.Leaves.Add(leaf);

            var parents = leaf.Candidates
                .Where(candidate => candidate.ParentProductId.HasValue)
                .Select(candidate => candidate.ParentProductId.Value)
                .Distinct()
                .ToList();

            var target = parents.Count == 1 ? model.Votes : model.Ambiguous;

            foreach (var parentId in parents)
                target[parentId] = target.GetValueOrDefault(parentId) + 1;
        }

        foreach (var model in plan.Models.Values)
        {
            if (existingModelParents.TryGetValue(model.Code, out var boundParentId) &&
                IsLiveProduct(plan, boundParentId))
            {
                model.ParentProductId = boundParentId;
                model.Status = AkeneoBindingStatus.AlreadyBound;
            }
            else if (conflictChoices != null &&
                     conflictChoices.TryGetValue(model.Code, out var chosenParentId) &&
                     (model.Votes.ContainsKey(chosenParentId) || model.Ambiguous.ContainsKey(chosenParentId)) &&
                     !IsUnanimous(model))
            {
                model.ParentProductId = chosenParentId;
                model.ResolvedByChoice = true;
                model.Status = AkeneoBindingStatus.Matched;
            }
            else if (IsUnanimous(model))
            {
                model.ParentProductId = model.Votes.Keys.Single();
                model.Status = AkeneoBindingStatus.Matched;
            }
            else if (model.Votes.Count == 0 && model.Ambiguous.Count == 0)
            {
                model.Status = AkeneoBindingStatus.Unmatched;
            }
            else
            {
                model.Status = AkeneoBindingStatus.Conflict;
            }

            foreach (var leaf in model.Leaves)
                ResolveModelLeaf(plan, model, leaf);
        }
    }

    /// <summary>One parent with votes, and no ambiguous variant pointing elsewhere.</summary>
    private static bool IsUnanimous(ModelPlan model) =>
        model.Votes.Count == 1 &&
        model.Ambiguous.Keys.All(model.Votes.ContainsKey);

    private static void ResolveModelLeaf(BindingPlan plan, ModelPlan model, LeafPlan leaf)
    {
        if (model.Status == AkeneoBindingStatus.Conflict)
        {
            leaf.Status = AkeneoBindingStatus.Conflict;
            return;
        }

        if (leaf.Candidates.Count == 0)
        {
            leaf.Status = AkeneoBindingStatus.Unmatched;
            return;
        }

        if (!model.ParentProductId.HasValue)
        {
            // Only standalone nopCommerce products matched under an unmatched model.
            leaf.Status = AkeneoBindingStatus.Issue;
            plan.AddIssue("Variant", leaf.Identifier, leaf.FamilyCode,
                $"In nopCommerce this SKU is a standalone product, not a variant. Akeneo places it under '{model.Code}'.");
            return;
        }

        var selected = leaf.Candidates
            .Where(candidate => candidate.ParentProductId == model.ParentProductId)
            .ToList();

        if (selected.Count == 1)
        {
            leaf.Selected = selected[0];
            leaf.Status = leaf.ExistingBinding
                ? AkeneoBindingStatus.AlreadyBound
                : AkeneoBindingStatus.Matched;
            return;
        }

        leaf.Status = AkeneoBindingStatus.Issue;

        var parentText = DescribeProduct(plan, model.ParentProductId.Value);

        plan.AddIssue("Variant", leaf.Identifier, leaf.FamilyCode, selected.Count > 1
            ? $"SKU matches {selected.Count} items under {parentText}."
            : $"SKU belongs to {string.Join(", ", leaf.Candidates.Select(candidate => candidate.ParentProductId.HasValue ? DescribeProduct(plan, candidate.ParentProductId.Value) : "a standalone product").Distinct())}, " +
              $"but '{model.Code}' is bound to {parentText}.");
    }

    private async Task ResolveAxisValuesAsync(
        BindingPlan plan,
        IDictionary<string, int> existingAxisValues,
        CancellationToken cancellationToken)
    {
        var combinationLeaves = plan.Leaves
            .Where(leaf =>
                leaf.Selected?.Kind == LeafKind.Combination &&
                leaf.FamilyConfiguration != null &&
                leaf.BindingModelCode != null &&
                plan.Models[leaf.BindingModelCode].ParentProductId.HasValue)
            .ToList();

        if (combinationLeaves.Count == 0)
            return;

        var parentIds = combinationLeaves
            .Select(leaf => plan.Models[leaf.BindingModelCode].ParentProductId.Value)
            .Distinct()
            .ToList();

        var attributeMappingsByParent = new Dictionary<int, List<ProductAttributeMapping>>();

        foreach (var chunk in parentIds.Chunk(QueryChunkSize))
        {
            var mappings = await productAttributeMappingRepository.Table
                .Where(mapping => chunk.Contains(mapping.ProductId))
                .ToListAsync();

            foreach (var mapping in mappings)
                AddToGroup(attributeMappingsByParent, mapping.ProductId, mapping);
        }

        var axisMappingCache = new Dictionary<int, IList<AkeneoFamilyVariantAxisMapping>>();

        foreach (var leaf in combinationLeaves)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var parentId = plan.Models[leaf.BindingModelCode].ParentProductId.Value;
            var combination = plan.CombinationsById[leaf.Selected.TargetId];
            var parentMappings = attributeMappingsByParent.GetValueOrDefault(parentId) ??
                                 new List<ProductAttributeMapping>();

            if (!axisMappingCache.TryGetValue(leaf.FamilyConfiguration.Id, out var axes))
            {
                axes = await familyMappingService.GetAxisMappingsAsync(leaf.FamilyConfiguration.Id);
                axisMappingCache[leaf.FamilyConfiguration.Id] = axes;
            }

            foreach (var axis in axes)
            {
                // Axes are never localizable or scopable in Akeneo, so no locale,
                // channel or currency is needed to read their option codes.
                if (!productValueResolver.TryGetValue(
                        leaf.EffectiveLeaf,
                        axis.AkeneoAttributeCode,
                        out var resolved))
                {
                    plan.AddIssue("AxisValue", leaf.Identifier, leaf.FamilyCode,
                        $"Variant has no value for axis '{axis.AkeneoAttributeCode}'.");
                    continue;
                }

                var axisValue = AkeneoVariantAxisValueFactory.Create(axis.AkeneoAttributeCode, resolved);

                if (axisValue == null)
                    continue;

                var attributeMapping = parentMappings.FirstOrDefault(mapping =>
                    mapping.ProductAttributeId == axis.NopProductAttributeId);

                if (attributeMapping == null)
                {
                    plan.AddIssue("AxisValue", leaf.Identifier, leaf.FamilyCode,
                        $"{DescribeProduct(plan, parentId)} has no product attribute #{axis.NopProductAttributeId} " +
                        $"for axis '{axis.AkeneoAttributeCode}'.");
                    continue;
                }

                var selectedValueIds = productAttributeParser
                    .ParseValues(combination.AttributesXml, attributeMapping.Id)
                    .Select(value => int.TryParse(value, out var id) ? id : 0)
                    .Where(id => id > 0)
                    .Distinct()
                    .ToList();

                if (selectedValueIds.Count != 1)
                {
                    plan.AddIssue("AxisValue", leaf.Identifier, leaf.FamilyCode,
                        $"Combination #{combination.Id} selects {selectedValueIds.Count} values for axis '{axis.AkeneoAttributeCode}'.");
                    continue;
                }

                var valueId = selectedValueIds[0];
                var mappingCode = AkeneoVariantAxisValueFactory.BuildMappingCode(parentId, axisValue);

                if (plan.ConflictingAxisCodes.Contains(mappingCode))
                    continue;

                if (plan.AxisValues.TryGetValue(mappingCode, out var planned))
                {
                    if (planned.ValueId != valueId)
                    {
                        plan.AxisValues.Remove(mappingCode);
                        plan.ConflictingAxisCodes.Add(mappingCode);
                        plan.AddIssue("AxisValue", mappingCode, leaf.FamilyCode,
                            $"Option resolves to different attribute values (#{planned.ValueId} and #{valueId}) on different combinations.");
                    }

                    continue;
                }

                if (existingAxisValues.TryGetValue(mappingCode, out var boundValueId))
                {
                    if (boundValueId == valueId)
                    {
                        plan.AxisValues[mappingCode] = new AxisPlan
                        {
                            MappingCode = mappingCode,
                            ValueId = valueId,
                            Status = AkeneoBindingStatus.AlreadyBound
                        };
                    }
                    else
                    {
                        plan.ConflictingAxisCodes.Add(mappingCode);
                        plan.AddIssue("AxisValue", mappingCode, leaf.FamilyCode,
                            $"Already bound to attribute value #{boundValueId}, but combination #{combination.Id} uses #{valueId}.");
                    }

                    continue;
                }

                plan.AxisValues[mappingCode] = new AxisPlan
                {
                    MappingCode = mappingCode,
                    ValueId = valueId,
                    Status = AkeneoBindingStatus.Matched
                };
            }
        }
    }

    #endregion

    #region Write

    private async Task<int> WritePlanAsync(BindingPlan plan, CancellationToken cancellationToken)
    {
        var written = 0;

        using var scope = new TransactionScope(
            TransactionScopeOption.Required,
            new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted },
            TransactionScopeAsyncFlowOption.Enabled);

        foreach (var model in plan.Models.Values.Where(model =>
                     model.Status == AkeneoBindingStatus.Matched))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await entityMappingService.UpsertAkeneoNopEntityMappingAsync(
                    AkeneoEntityType.ProductModel,
                    model.Code,
                    null,
                    NopEntityType.Product,
                    model.ParentProductId.Value))
            {
                written++;
            }
        }

        foreach (var leaf in plan.Leaves.Where(leaf =>
                     leaf.Status == AkeneoBindingStatus.Matched))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await entityMappingService.UpsertAkeneoNopEntityMappingAsync(
                    AkeneoEntityType.Product,
                    leaf.Identifier,
                    leaf.Uuid,
                    leaf.Selected.Kind == LeafKind.Combination
                        ? NopEntityType.ProductAttributeCombination
                        : NopEntityType.Product,
                    leaf.Selected.TargetId))
            {
                written++;
            }
        }

        foreach (var axis in plan.AxisValues.Values.Where(axis =>
                     axis.Status == AkeneoBindingStatus.Matched))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await entityMappingService.UpsertAkeneoNopEntityMappingAsync(
                    AkeneoEntityType.Option,
                    axis.MappingCode,
                    null,
                    NopEntityType.ProductAttributeValue,
                    axis.ValueId))
            {
                written++;
            }
        }

        scope.Complete();

        return written;
    }

    #endregion

    #region Report

    private async Task<AkeneoCatalogBindingReport> BuildReportAsync(
        BindingPlan plan,
        CancellationToken cancellationToken)
    {
        var report = new AkeneoCatalogBindingReport
        {
            FamilyCodes = plan.FamilyCodes
        };

        foreach (var model in plan.Models.Values)
            Count(report.Models, model.Status);

        foreach (var leaf in plan.Leaves)
            Count(report.Variants, leaf.Status);

        foreach (var axis in plan.AxisValues.Values)
            Count(report.AxisValues, axis.Status);

        report.AxisValues.Issue = plan.ConflictingAxisCodes.Count;

        foreach (var model in plan.Models.Values
                     .Where(model => model.Status == AkeneoBindingStatus.Conflict)
                     .OrderBy(model => model.FamilyCode)
                     .ThenBy(model => model.Code))
        {
            report.Conflicts.Add(new AkeneoBindingConflict
            {
                ProductModelCode = model.Code,
                FamilyCode = model.FamilyCode,
                VariantCount = model.Leaves.Count,
                Candidates = model.Votes.Keys
                    .Union(model.Ambiguous.Keys)
                    .Select(parentId =>
                    {
                        plan.ProductsById.TryGetValue(parentId, out var product);

                        return new AkeneoBindingCandidate
                        {
                            ProductId = parentId,
                            Name = product?.Name,
                            Sku = product?.Sku,
                            Published = product?.Published ?? false,
                            VariantVotes = model.Votes.GetValueOrDefault(parentId),
                            AmbiguousVariants = model.Ambiguous.GetValueOrDefault(parentId)
                        };
                    })
                    .OrderByDescending(candidate => candidate.VariantVotes)
                    .ThenByDescending(candidate => candidate.AmbiguousVariants)
                    .ToList()
            });
        }

        report.IssueTotal = plan.Issues.Count;
        report.Issues = plan.Issues.Take(MaxListedItems).ToList();

        // The unbound report is only meaningful against the whole catalog.
        if (plan.FamilyCodes.Count == 0)
            await AddUnboundProductsAsync(plan, report, cancellationToken);

        return report;
    }

    private static void Count(AkeneoBindingCounts counts, AkeneoBindingStatus status)
    {
        switch (status)
        {
            case AkeneoBindingStatus.Matched: counts.Matched++; break;
            case AkeneoBindingStatus.AlreadyBound: counts.AlreadyBound++; break;
            case AkeneoBindingStatus.Unmatched: counts.Unmatched++; break;
            case AkeneoBindingStatus.Conflict: counts.Conflict++; break;
            case AkeneoBindingStatus.Issue: counts.Issue++; break;
        }
    }

    private async Task AddUnboundProductsAsync(
        BindingPlan plan,
        AkeneoCatalogBindingReport report,
        CancellationToken cancellationToken)
    {
        var bound = new HashSet<int>(plan.ExistingBoundProductIds);

        foreach (var model in plan.Models.Values.Where(model => model.ParentProductId.HasValue))
            bound.Add(model.ParentProductId.Value);

        foreach (var leaf in plan.Leaves.Where(leaf => leaf.Selected != null))
        {
            if (leaf.Selected.Kind == LeafKind.Product)
                bound.Add(leaf.Selected.TargetId);

            if (leaf.Selected.ParentProductId.HasValue)
                bound.Add(leaf.Selected.ParentProductId.Value);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var published = await productRepository.Table
            .Where(product => !product.Deleted && product.Published)
            .OrderBy(product => product.Name)
            .Select(product => new
            {
                product.Id,
                product.Name,
                product.Sku,
                product.ProductTypeId
            })
            .ToListAsync();

        var unbound = published.Where(product => !bound.Contains(product.Id)).ToList();

        report.UnboundProductTotal = unbound.Count;
        report.UnboundProducts = unbound
            .Take(MaxListedItems)
            .Select(product => new AkeneoBindingNopProduct
            {
                Id = product.Id,
                Name = product.Name,
                Sku = product.Sku,
                ProductType = ((ProductType)product.ProductTypeId).ToString(),
                Published = true
            })
            .ToList();
    }

    private static IList<AkeneoBindingConflictResolution> BuildResolutions(BindingPlan plan) =>
        plan.Models.Values
            .Where(model => model.ResolvedByChoice)
            .OrderBy(model => model.Code)
            .Select(model => new AkeneoBindingConflictResolution
            {
                ProductModelCode = model.Code,
                Chosen = ToNopProduct(plan, model.ParentProductId.Value),
                NotChosen = model.Votes.Keys
                    .Union(model.Ambiguous.Keys)
                    .Where(parentId => parentId != model.ParentProductId)
                    .Select(parentId => ToNopProduct(plan, parentId))
                    .ToList()
            })
            .ToList();

    private static AkeneoBindingNopProduct ToNopProduct(BindingPlan plan, int productId)
    {
        plan.ProductsById.TryGetValue(productId, out var product);

        return new AkeneoBindingNopProduct
        {
            Id = productId,
            Name = product?.Name,
            Sku = product?.Sku,
            ProductType = product?.ProductType.ToString(),
            Published = product?.Published ?? false
        };
    }

    #endregion

    #region Data helpers

    private static async Task<List<AkeneoProductDefinition>> ReadAllAsync(
        Func<string, CancellationToken, Task<AkeneoProductPageResult>> readPageAsync,
        CancellationToken cancellationToken)
    {
        var items = new List<AkeneoProductDefinition>();
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        string searchAfter = null;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();

            var page = await readPageAsync(searchAfter, cancellationToken);
            items.AddRange(page.Items);

            searchAfter = page.HasNextPage && seenCursors.Add(page.SearchAfter)
                ? page.SearchAfter
                : null;
        }
        while (searchAfter != null);

        return items;
    }

    private static string BuildFamilySearchJson(IList<string> familyCodes)
    {
        if (familyCodes == null || familyCodes.Count == 0)
            return null;

        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["family"] = new[]
            {
                new Dictionary<string, object>
                {
                    ["operator"] = "IN",
                    ["value"] = familyCodes
                }
            }
        });
    }

    private async Task LoadProductsByIdAsync(
        BindingPlan plan,
        IEnumerable<int> productIds,
        CancellationToken cancellationToken)
    {
        var missing = productIds
            .Where(id => id > 0 && !plan.ProductsById.ContainsKey(id))
            .Distinct()
            .ToList();

        foreach (var chunk in missing.Chunk(QueryChunkSize))
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var product in await productRepository.Table
                         .Where(product => chunk.Contains(product.Id))
                         .ToListAsync())
            {
                plan.ProductsById[product.Id] = product;
            }
        }
    }

    private async Task LoadCombinationsByIdAsync(
        BindingPlan plan,
        IEnumerable<int> combinationIds,
        CancellationToken cancellationToken)
    {
        var missing = combinationIds
            .Where(id => id > 0 && !plan.CombinationsById.ContainsKey(id))
            .Distinct()
            .ToList();

        foreach (var chunk in missing.Chunk(QueryChunkSize))
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var combination in await combinationRepository.Table
                         .Where(combination => chunk.Contains(combination.Id))
                         .ToListAsync())
            {
                plan.CombinationsById[combination.Id] = combination;
            }
        }
    }

    /// <summary>
    /// Child product id → nopCommerce products that list it as an
    /// "associated to product" attribute value.
    /// </summary>
    private async Task<Dictionary<int, List<int>>> LoadAssociatedParentsAsync(
        IList<int> childProductIds,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<int, List<int>>();

        foreach (var chunk in childProductIds.Chunk(QueryChunkSize))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var rows = await (
                    from value in productAttributeValueRepository.Table
                    join mapping in productAttributeMappingRepository.Table
                        on value.ProductAttributeMappingId equals mapping.Id
                    where value.AttributeValueTypeId == (int)AttributeValueType.AssociatedToProduct &&
                          chunk.Contains(value.AssociatedProductId)
                    select new { value.AssociatedProductId, mapping.ProductId })
                .ToListAsync();

            foreach (var row in rows)
            {
                if (!result.TryGetValue(row.AssociatedProductId, out var parents))
                {
                    parents = new List<int>();
                    result[row.AssociatedProductId] = parents;
                }

                if (!parents.Contains(row.ProductId))
                    parents.Add(row.ProductId);
            }
        }

        return result;
    }

    private static bool IsLiveProduct(BindingPlan plan, int productId) =>
        plan.ProductsById.TryGetValue(productId, out var product) && !product.Deleted;

    private static string DescribeProduct(BindingPlan plan, int productId) =>
        plan.ProductsById.TryGetValue(productId, out var product)
            ? $"#{productId} '{product.Name}'"
            : $"#{productId}";

    private static int? Lookup(IDictionary<string, int> map, string key) =>
        !string.IsNullOrWhiteSpace(key) && map.TryGetValue(key, out var id) ? id : null;

    private static void AddToGroup<TKey, TValue>(
        IDictionary<TKey, List<TValue>> groups,
        TKey key,
        TValue value)
    {
        if (key == null)
            return;

        if (!groups.TryGetValue(key, out var group))
        {
            group = new List<TValue>();
            groups[key] = group;
        }

        group.Add(value);
    }

    #endregion
}
