using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Import;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public class AkeneoProductSpecificationSynchronizer(
    ISpecificationAttributeService specificationAttributeService,
    IAkeneoNopEntityMappingService entityMappingService,
    IAkeneoManagedRelationService managedRelationService)
    : IAkeneoProductSectionSynchronizer
{
    public int Order => 400;

    public async Task SynchronizeAsync(
        AkeneoProductSyncContext context,
        CancellationToken cancellationToken = default)
    {
        if (context.Product is null ||
            context.Request.SpecificationAttributeSyncMode == AkeneoCollectionSyncMode.Disabled)
        {
            return;
        }

        var mappings = context
            .GetMappings(NopTargetType.SpecificationAttribute)
            .ToList();

        var changed = false;

        foreach (var mapped in mappings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            changed |= await SynchronizeMappingAsync(context, mapped);
        }

        if (changed)
            context.MarkChanged();
    }

    private async Task<bool> SynchronizeMappingAsync(
        AkeneoProductSyncContext context,
        AkeneoResolvedMappedValue mapped)
    {
        var specificationAttributeId = mapped.Mapping.NopTargetEntityId ?? 0;

        if (specificationAttributeId <= 0)
        {
            context.Result.AddWarning(
                $"Specification mapping has no nopCommerce specification attribute. Akeneo attribute: {mapped.Mapping.AkeneoAttributeCode}");
            return false;
        }

        var desiredAssignmentIds = new HashSet<int>();
        var desiredStateComplete = true;
        var changed = false;

        foreach (var optionItem in mapped.HasValue
                     ? AkeneoSyncValueHelper.GetOptionItems(mapped)
                     : Array.Empty<AkeneoResolvedOptionItem>())
        {
            var optionResult = await GetOrCreateOptionAsync(
                specificationAttributeId,
                mapped.Mapping.AkeneoAttributeCode,
                optionItem.AkeneoOptionCode,
                optionItem.DisplayName,
                context.Request.CreateMissingSpecificationAttributeOptions);

            if (optionResult.Option == null)
            {
                desiredStateComplete = false;
                context.Result.AddWarning(
                    $"Specification option '{optionItem.DisplayName}' was not found and could not be created. Akeneo attribute: {mapped.Mapping.AkeneoAttributeCode}");
                continue;
            }

            changed |= optionResult.Changed;

            var assignmentResult = await GetOrCreateAssignmentAsync(
                context.Product.Id,
                optionResult.Option.Id);

            desiredAssignmentIds.Add(assignmentResult.Assignment.Id);
            changed |= assignmentResult.Created;

            if (assignmentResult.Created && context.Request.SyncProfileId.HasValue)
            {
                await managedRelationService.UpsertAsync(
                    context.Request.SyncProfileId.Value,
                    context.Request.SyncRunRecordId,
                    context.Product.Id,
                    AkeneoManagedRelationType.ProductSpecificationAssignment,
                    assignmentResult.Assignment.Id,
                    mapped.Mapping.AkeneoAttributeCode,
                    optionItem.AkeneoOptionCode ?? optionItem.DisplayName);
            }
        }

        if (context.Request.SpecificationAttributeSyncMode == AkeneoCollectionSyncMode.Merge)
            return changed;

        if (!desiredStateComplete)
        {
            context.Result.AddWarning(
                $"Specification removal for '{mapped.Mapping.AkeneoAttributeCode}' was skipped because the desired state was incomplete.");
            return changed;
        }

        var currentAssignments = await specificationAttributeService
            .GetProductSpecificationAttributesAsync(context.Product.Id);

        if (context.Request.SpecificationAttributeSyncMode == AkeneoCollectionSyncMode.ReplaceAll)
        {
            foreach (var assignment in currentAssignments)
            {
                if (desiredAssignmentIds.Contains(assignment.Id))
                    continue;

                var option = await specificationAttributeService
                    .GetSpecificationAttributeOptionByIdAsync(
                        assignment.SpecificationAttributeOptionId);

                if (option?.SpecificationAttributeId != specificationAttributeId)
                    continue;

                await specificationAttributeService
                    .DeleteProductSpecificationAttributeAsync(assignment);
                changed = true;
            }

            return changed;
        }

        if (!context.Request.SyncProfileId.HasValue)
            return changed;

        var managed = await managedRelationService.GetByProductAsync(
            context.Request.SyncProfileId.Value,
            context.Product.Id,
            AkeneoManagedRelationType.ProductSpecificationAssignment,
            mapped.Mapping.AkeneoAttributeCode);

        foreach (var relation in managed)
        {
            if (desiredAssignmentIds.Contains(relation.NopRelationEntityId))
                continue;

            var assignment = currentAssignments.FirstOrDefault(item =>
                item.Id == relation.NopRelationEntityId);

            if (assignment != null)
            {
                await specificationAttributeService
                    .DeleteProductSpecificationAttributeAsync(assignment);
                changed = true;
            }

            await managedRelationService.DeleteAsync(relation);
        }

        return changed;
    }

    private async Task<SpecificationAssignmentResult> GetOrCreateAssignmentAsync(
        int productId,
        int optionId)
    {
        var existing = await specificationAttributeService
            .GetProductSpecificationAttributesAsync(
                productId,
                specificationAttributeOptionId: optionId);

        var assignment = existing.FirstOrDefault(item =>
            item.SpecificationAttributeOptionId == optionId);

        if (assignment != null)
            return new SpecificationAssignmentResult(assignment, false);

        assignment = new ProductSpecificationAttribute
        {
            ProductId = productId,
            AttributeTypeId = (int)SpecificationAttributeType.Option,
            SpecificationAttributeOptionId = optionId,
            AllowFiltering = true,
            ShowOnProductPage = true,
            DisplayOrder = 0
        };

        await specificationAttributeService.InsertProductSpecificationAttributeAsync(assignment);
        return new SpecificationAssignmentResult(assignment, true);
    }

    private async Task<SpecificationOptionResult> GetOrCreateOptionAsync(
        int specificationAttributeId,
        string akeneoAttributeCode,
        string akeneoOptionCode,
        string optionName,
        bool createMissing)
    {
        if (string.IsNullOrWhiteSpace(optionName) &&
            string.IsNullOrWhiteSpace(akeneoOptionCode))
        {
            return new SpecificationOptionResult(null, false);
        }

        optionName = optionName?.Trim() ?? akeneoOptionCode?.Trim();
        var mappingCode = BuildOptionMappingCode(
            specificationAttributeId,
            akeneoAttributeCode,
            akeneoOptionCode ?? optionName);

        var mappedOptionId = await entityMappingService
            .GetMappedNopEntityIdByAkeneoCodeAsync(
                AkeneoEntityType.Option,
                mappingCode,
                NopEntityType.SpecificationAttributeOption);

        if (mappedOptionId.HasValue)
        {
            var mappedOption = await specificationAttributeService
                .GetSpecificationAttributeOptionByIdAsync(mappedOptionId.Value);

            if (mappedOption != null &&
                mappedOption.SpecificationAttributeId == specificationAttributeId)
            {
                if (!string.Equals(mappedOption.Name, optionName, StringComparison.Ordinal))
                {
                    mappedOption.Name = optionName;
                    await specificationAttributeService
                        .UpdateSpecificationAttributeOptionAsync(mappedOption);
                    return new SpecificationOptionResult(mappedOption, true);
                }

                return new SpecificationOptionResult(mappedOption, false);
            }
        }

        var existingOptions = await specificationAttributeService
            .GetSpecificationAttributeOptionsBySpecificationAttributeAsync(
                specificationAttributeId);

        var existing = existingOptions.FirstOrDefault(option =>
            string.Equals(option.Name, optionName, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            await entityMappingService.UpsertAkeneoNopEntityMappingAsync(
                AkeneoEntityType.Option,
                mappingCode,
                null,
                NopEntityType.SpecificationAttributeOption,
                existing.Id);

            return new SpecificationOptionResult(existing, false);
        }

        if (!createMissing)
            return new SpecificationOptionResult(null, false);

        var created = new SpecificationAttributeOption
        {
            SpecificationAttributeId = specificationAttributeId,
            Name = optionName,
            DisplayOrder = 0
        };

        await specificationAttributeService.InsertSpecificationAttributeOptionAsync(created);

        await entityMappingService.UpsertAkeneoNopEntityMappingAsync(
            AkeneoEntityType.Option,
            mappingCode,
            null,
            NopEntityType.SpecificationAttributeOption,
            created.Id);

        return new SpecificationOptionResult(created, true);
    }

    private static string BuildOptionMappingCode(
        int specificationAttributeId,
        string akeneoAttributeCode,
        string optionCode) =>
        $"spec:{specificationAttributeId}:{akeneoAttributeCode?.Trim()}:{optionCode?.Trim()}";

    private sealed record SpecificationAssignmentResult(
        ProductSpecificationAttribute Assignment,
        bool Created);

    private sealed record SpecificationOptionResult(
        SpecificationAttributeOption Option,
        bool Changed);
}
