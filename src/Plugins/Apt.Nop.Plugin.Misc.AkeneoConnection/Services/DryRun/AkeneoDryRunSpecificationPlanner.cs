using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;
using static Apt.Nop.Plugin.Misc.AkeneoConnection.Services.DryRun.AkeneoDryRunPlanHelper;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services.DryRun;

public sealed class AkeneoDryRunSpecificationPlanner(
    ISpecificationAttributeService specificationAttributeService,
    IAkeneoNopEntityMappingService entityMappingService,
    IAkeneoManagedRelationService managedRelationService)
    : IAkeneoDryRunSectionPlanner
{
    public int Order => 400;

    public async Task PlanAsync(
        AkeneoProductSyncContext context,
        AkeneoProductMappingPreviewModel model,
        CancellationToken cancellationToken = default)
    {
        if (context.Request.SpecificationAttributeSyncMode == AkeneoCollectionSyncMode.Disabled)
        {
            model.CoverageNotes.Add(
                "Specification-attribute synchronization is disabled by the saved sync profile.");
            return;
        }

        var definitions = await specificationAttributeService.GetAllSpecificationAttributesAsync();
        var product = context.ExistingProduct;
        var currentAssignments = product == null
            ? new List<ProductSpecificationAttribute>()
            : (await specificationAttributeService
                .GetProductSpecificationAttributesAsync(product.Id)).ToList();

        foreach (var mapped in context.GetMappings(NopTargetType.SpecificationAttribute))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var attributeId = mapped.Mapping.NopTargetEntityId ?? 0;
            var definition = definitions.FirstOrDefault(item => item.Id == attributeId);
            var sourceCode = AkeneoMappingHelper.GetSourceMappingCode(mapped.Mapping);
            var sourceName = GetSourceName(mapped);

            if (attributeId <= 0 || definition == null)
            {
                AddReviewOperation(
                    model,
                    "Specification attributes",
                    attributeId > 0 ? $"Specification attribute #{attributeId}" : "Missing target",
                    sourceName,
                    "The target nopCommerce specification attribute is missing.",
                    mapped.Mapping.IsRequired);
                continue;
            }

            var options = await specificationAttributeService
                .GetSpecificationAttributeOptionsBySpecificationAttributeAsync(attributeId);

            var currentForAttribute = new List<(ProductSpecificationAttribute Assignment, SpecificationAttributeOption Option)>();
            foreach (var assignment in currentAssignments)
            {
                var option = options.FirstOrDefault(item =>
                    item.Id == assignment.SpecificationAttributeOptionId);
                if (option != null)
                    currentForAttribute.Add((assignment, option));
            }

            var desiredAssignmentIds = new HashSet<int>();
            var desiredStateComplete = true;

            foreach (var item in mapped.HasValue
                         ? AkeneoSyncValueHelper.GetOptionItems(mapped)
                         : Array.Empty<AkeneoResolvedOptionItem>())
            {
                var displayName = item.DisplayName?.Trim() ?? item.AkeneoOptionCode?.Trim();
                if (string.IsNullOrWhiteSpace(displayName))
                    continue;

                var mappingCode =
                    $"spec:{attributeId}:{sourceCode?.Trim()}:{(item.AkeneoOptionCode ?? displayName).Trim()}";

                var mappedOptionId = await entityMappingService
                    .GetMappedNopEntityIdByAkeneoCodeAsync(
                        AkeneoEntityType.Option,
                        mappingCode,
                        NopEntityType.SpecificationAttributeOption);

                SpecificationAttributeOption option = null;
                if (mappedOptionId.HasValue)
                {
                    option = options.FirstOrDefault(candidate =>
                        candidate.Id == mappedOptionId.Value);
                }

                option ??= options.FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, displayName, StringComparison.OrdinalIgnoreCase));

                if (option == null)
                {
                    if (!context.Request.CreateMissingSpecificationAttributeOptions)
                    {
                        desiredStateComplete = false;
                        AddReviewOperation(
                            model,
                            "Specification attributes",
                            $"{definition.Name}: {displayName}",
                            sourceName,
                            "The option does not exist and the profile does not allow missing specification options to be created.",
                            mapped.Mapping.IsRequired,
                            "Not assigned",
                            "Not assigned");
                        continue;
                    }

                    AddOperation(
                        model,
                        "Specification attributes",
                        $"{definition.Name}: {displayName}",
                        sourceName,
                        "Option/assignment absent",
                        "Create option and assign",
                        AkeneoDryRunOperationType.Add,
                        "The option and its product assignment would be created.",
                        mapped.Mapping.IsRequired);
                    continue;
                }

                if (!string.Equals(option.Name, displayName, StringComparison.Ordinal))
                {
                    AddOperation(
                        model,
                        "Specification attributes",
                        $"{definition.Name} option",
                        sourceName,
                        option.Name,
                        displayName,
                        AkeneoDryRunOperationType.Update,
                        "The Akeneo-mapped option would be renamed.",
                        mapped.Mapping.IsRequired);
                }

                var currentAssignment = currentForAttribute
                    .FirstOrDefault(current => current.Option.Id == option.Id);
                var assigned = currentAssignment.Assignment != null;

                if (assigned)
                    desiredAssignmentIds.Add(currentAssignment.Assignment.Id);

                AddOperation(
                    model,
                    "Specification attributes",
                    $"{definition.Name}: {displayName}",
                    sourceName,
                    assigned ? "Assigned" : "Not assigned",
                    "Assigned",
                    assigned
                        ? AkeneoDryRunOperationType.NoChange
                        : AkeneoDryRunOperationType.Add,
                    null,
                    mapped.Mapping.IsRequired);
            }

            if (context.Request.SpecificationAttributeSyncMode == AkeneoCollectionSyncMode.Merge)
                continue;

            if (!desiredStateComplete)
            {
                model.CoverageNotes.Add(
                    $"Stale specification assignments for '{definition.Name}' are not planned for removal because its desired state is incomplete.");
                continue;
            }

            IEnumerable<(ProductSpecificationAttribute Assignment, SpecificationAttributeOption Option)> stale;

            if (context.Request.SpecificationAttributeSyncMode == AkeneoCollectionSyncMode.ReplaceAll)
            {
                stale = currentForAttribute.Where(current =>
                    !desiredAssignmentIds.Contains(current.Assignment.Id));
            }
            else if (context.Request.SyncProfileId.HasValue && product != null)
            {
                var managed = await managedRelationService.GetByProductAsync(
                    context.Request.SyncProfileId.Value,
                    product.Id,
                    AkeneoManagedRelationType.ProductSpecificationAssignment,
                    sourceCode);

                var managedIds = managed.Select(relation => relation.NopRelationEntityId).ToHashSet();
                stale = currentForAttribute.Where(current =>
                    managedIds.Contains(current.Assignment.Id) &&
                    !desiredAssignmentIds.Contains(current.Assignment.Id));
            }
            else
            {
                continue;
            }

            foreach (var current in stale)
            {
                AddOperation(
                    model,
                    "Specification attributes",
                    $"{definition.Name}: {current.Option.Name}",
                    sourceName,
                    "Assigned",
                    "Removed",
                    AkeneoDryRunOperationType.Remove,
                    context.Request.SpecificationAttributeSyncMode == AkeneoCollectionSyncMode.ReplaceManaged
                        ? "This assignment is owned by the plugin and is stale for this mapping."
                        : "Replace-all makes this specification attribute match the desired Akeneo values.",
                    mapped.Mapping.IsRequired);
            }
        }
    }
}
