using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services.Planning;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public partial class AkeneoProductSpecificationSynchronizer
    : IAkeneoProductSectionSynchronizer,
      IAkeneoSectionDryRunPlanProvider
{
    private readonly ISpecificationAttributeService _specificationAttributeService;
    private readonly IAkeneoNopEntityMappingService _entityMappingService;
    private readonly IAkeneoManagedRelationService _managedRelationService;

    public AkeneoProductSpecificationSynchronizer(
        ISpecificationAttributeService specificationAttributeService,
        IAkeneoNopEntityMappingService entityMappingService,
        IAkeneoManagedRelationService managedRelationService)
    {
        _specificationAttributeService = specificationAttributeService;
        _entityMappingService = entityMappingService;
        _managedRelationService = managedRelationService;
    }

    public int Order => 400;

    public async Task SynchronizeAsync(
        AkeneoProductSyncContext context,
        CancellationToken cancellationToken = default)
    {
        var plan = await BuildPlanAsync(context, cancellationToken);
        await plan.ExecuteAsync(context, cancellationToken);
    }

    private async Task<AkeneoExecutableSectionPlan> BuildPlanAsync(
        AkeneoProductSyncContext context,
        CancellationToken cancellationToken)
    {
        var plan = new AkeneoExecutableSectionPlan();

        if (context.Request.SpecificationAttributeSyncMode ==
            AkeneoCollectionSyncMode.Disabled)
        {
            plan.CoverageNotes.Add(
                "Specification-attribute synchronization is disabled by the saved sync profile.");
            return plan;
        }

        var product = context.Product ?? context.ExistingProduct;
        var definitions = await _specificationAttributeService
            .GetAllSpecificationAttributesAsync();
        var currentAssignments = product == null
            ? new List<ProductSpecificationAttribute>()
            : (await _specificationAttributeService
                .GetProductSpecificationAttributesAsync(product.Id)).ToList();

        foreach (var mapped in context.GetMappings(NopTargetType.SpecificationAttribute))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var specificationAttributeId = mapped.Mapping.NopTargetEntityId ?? 0;
            var sourceMappingCode =
                AkeneoMappingHelper.GetSourceMappingCode(mapped.Mapping);
            var sourceDisplayName =
                AkeneoMappingHelper.GetSourceDisplayName(mapped.Mapping);
            var definition = definitions.FirstOrDefault(item =>
                item.Id == specificationAttributeId);

            if (specificationAttributeId <= 0 || definition == null)
            {
                plan.AddReview(
                    "Specification attributes",
                    specificationAttributeId > 0
                        ? $"Specification attribute #{specificationAttributeId}"
                        : "Missing target",
                    sourceDisplayName,
                    $"Specification mapping has no valid nopCommerce specification attribute. Akeneo source: {sourceDisplayName}",
                    mapped.Mapping.IsRequired);
                continue;
            }

            var options = (await _specificationAttributeService
                .GetSpecificationAttributeOptionsBySpecificationAttributeAsync(
                    specificationAttributeId)).ToList();
            var desiredAssignmentIds = new HashSet<int>();
            var desiredStateComplete = true;
            var plannedNewOptionsByName =
                new Dictionary<string, SpecificationDesiredValueState>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (var optionItem in mapped.HasValue
                         ? AkeneoSyncValueHelper.GetOptionItems(mapped)
                         : Array.Empty<AkeneoResolvedOptionItem>())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var optionName = optionItem.DisplayName?.Trim() ??
                                 optionItem.AkeneoOptionCode?.Trim();

                if (string.IsNullOrWhiteSpace(optionName))
                    continue;

                var mappingCode = BuildOptionMappingCode(
                    specificationAttributeId,
                    sourceMappingCode,
                    optionItem.AkeneoOptionCode ?? optionName);
                var state = new SpecificationDesiredValueState
                {
                    MappingCode = mappingCode,
                    OptionCode = optionItem.AkeneoOptionCode,
                    OptionName = optionName
                };
                var reusedPlannedOption = false;

                var mappedOptionId = await _entityMappingService
                    .GetMappedNopEntityIdByAkeneoCodeAsync(
                        AkeneoEntityType.Option,
                        mappingCode,
                        NopEntityType.SpecificationAttributeOption);

                if (mappedOptionId.HasValue)
                {
                    var mappedOption = await _specificationAttributeService
                        .GetSpecificationAttributeOptionByIdAsync(mappedOptionId.Value);

                    if (mappedOption != null &&
                        mappedOption.SpecificationAttributeId == specificationAttributeId)
                    {
                        state.Option = mappedOption;
                        state.RenameOption = !string.Equals(
                            mappedOption.Name,
                            optionName,
                            StringComparison.Ordinal);
                    }
                }

                if (state.Option == null)
                {
                    if (plannedNewOptionsByName.TryGetValue(optionName, out var plannedState))
                    {
                        state = plannedState;
                        reusedPlannedOption = true;
                    }
                    else
                    {
                        state.Option = options.FirstOrDefault(option =>
                            string.Equals(
                                option.Name,
                                optionName,
                                StringComparison.OrdinalIgnoreCase));

                        if (state.Option != null)
                        {
                            state.LinkOptionMapping = true;
                        }
                        else if (context.Request.CreateMissingSpecificationAttributeOptions)
                        {
                            state.CreateOption = true;
                            plannedNewOptionsByName[optionName] = state;
                        }
                        else
                        {
                        desiredStateComplete = false;
                        plan.AddReview(
                            "Specification attributes",
                            $"{definition.Name}: {optionName}",
                            sourceDisplayName,
                            $"Specification option '{optionName}' was not found and the profile does not allow it to be created. Akeneo source: {sourceDisplayName}",
                            mapped.Mapping.IsRequired,
                            "Option absent",
                            "Option absent");
                            continue;
                        }
                    }
                }

                if (reusedPlannedOption)
                {
                    plan.AddInternalOperation(async _ =>
                    {
                        if (state.Option == null)
                            return false;

                        await _entityMappingService
                            .UpsertAkeneoNopEntityMappingAsync(
                                AkeneoEntityType.Option,
                                mappingCode,
                                null,
                                NopEntityType.SpecificationAttributeOption,
                                state.Option.Id);
                        return false;
                    });
                }
                else if (state.CreateOption)
                {
                    plan.AddOperation(
                        "Specification attributes",
                        $"{definition.Name} option: {optionName}",
                        sourceDisplayName,
                        "Option absent",
                        "Create option",
                        AkeneoDryRunOperationType.Add,
                        "The missing specification option would be created and linked to its Akeneo option code.",
                        mapped.Mapping.IsRequired,
                        async _ =>
                        {
                            state.Option = new SpecificationAttributeOption
                            {
                                SpecificationAttributeId = specificationAttributeId,
                                Name = optionName,
                                DisplayOrder = 0
                            };

                            await _specificationAttributeService
                                .InsertSpecificationAttributeOptionAsync(state.Option);
                            await _entityMappingService
                                .UpsertAkeneoNopEntityMappingAsync(
                                    AkeneoEntityType.Option,
                                    mappingCode,
                                    null,
                                    NopEntityType.SpecificationAttributeOption,
                                    state.Option.Id);

                            return true;
                        });
                }
                else if (state.RenameOption)
                {
                    plan.AddOperation(
                        "Specification attributes",
                        $"{definition.Name} option",
                        sourceDisplayName,
                        state.Option.Name,
                        optionName,
                        AkeneoDryRunOperationType.Update,
                        "The Akeneo-mapped specification option would be renamed.",
                        mapped.Mapping.IsRequired,
                        async _ =>
                        {
                            state.Option.Name = optionName;
                            await _specificationAttributeService
                                .UpdateSpecificationAttributeOptionAsync(state.Option);
                            return true;
                        });
                }
                else if (state.LinkOptionMapping)
                {
                    plan.AddInternalOperation(async _ =>
                    {
                        await _entityMappingService
                            .UpsertAkeneoNopEntityMappingAsync(
                                AkeneoEntityType.Option,
                                mappingCode,
                                null,
                                NopEntityType.SpecificationAttributeOption,
                                state.Option.Id);
                        return false;
                    });
                }

                if (state.Option != null)
                {
                    state.Assignment = currentAssignments.FirstOrDefault(item =>
                        item.SpecificationAttributeOptionId == state.Option.Id);
                }

                if (state.Assignment != null)
                {
                    desiredAssignmentIds.Add(state.Assignment.Id);

                    if (state.AssignmentOperationPlanned)
                        continue;

                    state.AssignmentOperationPlanned = true;
                    plan.AddOperation(
                        "Specification attributes",
                        $"{definition.Name}: {optionName}",
                        sourceDisplayName,
                        "Assigned",
                        "Assigned",
                        AkeneoDryRunOperationType.NoChange,
                        isRequired: mapped.Mapping.IsRequired);
                }
                else
                {
                    if (state.AssignmentOperationPlanned)
                        continue;

                    state.AssignmentOperationPlanned = true;
                    plan.AddOperation(
                        "Specification attributes",
                        $"{definition.Name}: {optionName}",
                        sourceDisplayName,
                        "Not assigned",
                        "Assigned",
                        AkeneoDryRunOperationType.Add,
                        "The specification option would be assigned to the product.",
                        mapped.Mapping.IsRequired,
                        async _ =>
                        {
                            if (product == null || state.Option == null)
                                return false;

                            state.Assignment = new ProductSpecificationAttribute
                            {
                                ProductId = product.Id,
                                AttributeTypeId = (int)SpecificationAttributeType.Option,
                                SpecificationAttributeOptionId = state.Option.Id,
                                AllowFiltering = true,
                                ShowOnProductPage = true,
                                DisplayOrder = 0
                            };

                            await _specificationAttributeService
                                .InsertProductSpecificationAttributeAsync(state.Assignment);

                            if (context.Request.SyncProfileId.HasValue)
                            {
                                await _managedRelationService.UpsertAsync(
                                    context.Request.SyncProfileId.Value,
                                    context.Request.SyncRunRecordId,
                                    product.Id,
                                    AkeneoManagedRelationType.ProductSpecificationAssignment,
                                    state.Assignment.Id,
                                    sourceMappingCode,
                                    optionItem.AkeneoOptionCode ?? optionName);
                            }

                            return true;
                        });
                }
            }

            if (context.Request.SpecificationAttributeSyncMode ==
                AkeneoCollectionSyncMode.Merge)
            {
                continue;
            }

            if (!desiredStateComplete)
            {
                var warning =
                    $"Specification removal for '{sourceDisplayName}' was skipped because the desired state was incomplete.";
                plan.Warnings.Add(warning);
                plan.CoverageNotes.Add(
                    $"Stale specification assignments for '{definition.Name}' are not planned for removal because its desired state is incomplete.");
                continue;
            }

            if (context.Request.SpecificationAttributeSyncMode ==
                AkeneoCollectionSyncMode.ReplaceAll)
            {
                foreach (var assignment in currentAssignments)
                {
                    if (desiredAssignmentIds.Contains(assignment.Id))
                        continue;

                    var option = options.FirstOrDefault(item =>
                        item.Id == assignment.SpecificationAttributeOptionId);

                    if (option?.SpecificationAttributeId != specificationAttributeId)
                        continue;

                    plan.AddOperation(
                        "Specification attributes",
                        $"{definition.Name}: {option.Name}",
                        sourceDisplayName,
                        "Assigned",
                        "Removed",
                        AkeneoDryRunOperationType.Remove,
                        "Replace-all makes this specification attribute match the desired Akeneo options.",
                        mapped.Mapping.IsRequired,
                        async _ =>
                        {
                            await _specificationAttributeService
                                .DeleteProductSpecificationAttributeAsync(assignment);
                            return true;
                        });
                }

                continue;
            }

            if (!context.Request.SyncProfileId.HasValue || product == null)
            {
                plan.CoverageNotes.Add(
                    $"Replace-managed specification removals for '{definition.Name}' require a saved sync profile and cannot be planned without one.");
                continue;
            }

            var managed = await _managedRelationService.GetByProductAsync(
                context.Request.SyncProfileId.Value,
                product.Id,
                AkeneoManagedRelationType.ProductSpecificationAssignment,
                sourceMappingCode);

            foreach (var relation in managed)
            {
                if (desiredAssignmentIds.Contains(relation.NopRelationEntityId))
                    continue;

                var assignment = currentAssignments.FirstOrDefault(item =>
                    item.Id == relation.NopRelationEntityId);

                if (assignment == null)
                {
                    plan.AddInternalOperation(async _ =>
                    {
                        await _managedRelationService.DeleteAsync(relation);
                        return false;
                    });
                    continue;
                }

                var option = options.FirstOrDefault(item =>
                    item.Id == assignment.SpecificationAttributeOptionId);

                var optionDisplayName = option?.Name ??
                    $"Option #{assignment.SpecificationAttributeOptionId}";

                plan.AddOperation(
                    "Specification attributes",
                    $"{definition.Name}: {optionDisplayName}",
                    sourceDisplayName,
                    "Assigned",
                    "Removed",
                    AkeneoDryRunOperationType.Remove,
                    "This specification assignment is plugin-owned and stale for this mapping.",
                    mapped.Mapping.IsRequired,
                    async _ =>
                    {
                        await _specificationAttributeService
                            .DeleteProductSpecificationAttributeAsync(assignment);
                        await _managedRelationService.DeleteAsync(relation);
                        return true;
                    });
            }
        }

        return plan;
    }

    private static string BuildOptionMappingCode(
        int specificationAttributeId,
        string akeneoAttributeCode,
        string optionCode) =>
        $"spec:{specificationAttributeId}:{akeneoAttributeCode?.Trim()}:{optionCode?.Trim()}";

    private sealed class SpecificationDesiredValueState
    {
        public string MappingCode { get; init; }

        public string OptionCode { get; init; }

        public string OptionName { get; init; }

        public SpecificationAttributeOption Option { get; set; }

        public ProductSpecificationAttribute Assignment { get; set; }

        public bool CreateOption { get; set; }

        public bool RenameOption { get; set; }

        public bool LinkOptionMapping { get; set; }

        public bool AssignmentOperationPlanned { get; set; }
    }
}
