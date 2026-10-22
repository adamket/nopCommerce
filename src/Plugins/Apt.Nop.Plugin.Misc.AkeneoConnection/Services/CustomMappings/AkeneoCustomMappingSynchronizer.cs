using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Models;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services.Planning;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services.CustomMappings;

/// <summary>Dispatches explicitly mapped handlers through the shared plan executor.</summary>
public sealed class AkeneoCustomMappingSynchronizer(AkeneoCustomMappingRegistry registry, IAkeneoApiClient api)
    : IAkeneoProductSectionSynchronizer, IAkeneoSectionDryRunPlanProvider
{
    private readonly Dictionary<string, AkeneoAttributeDefinition> _attributes = new(StringComparer.OrdinalIgnoreCase);
    public int Order => 640;
    public async Task SynchronizeAsync(AkeneoProductSyncContext context, CancellationToken cancellationToken = default)
    {
        if (context.Product == null) return;
        var plan = await BuildPlanAsync(context, cancellationToken);
        if (plan.Operations.Any(o => o.Preview is { ChangeType: AkeneoDryRunOperationType.Review, IsRequired: true }))
        {
            foreach (var warning in plan.Warnings) context.Result.AddError(warning);
            return;
        }
        await plan.ExecuteAsync(context, cancellationToken);
    }
    public async Task PlanAsync(AkeneoProductSyncContext context, AkeneoProductMappingPreviewModel model,
        CancellationToken cancellationToken = default) =>
        (await BuildPlanAsync(context, cancellationToken)).Render(model);

    private async Task<AkeneoExecutableSectionPlan> BuildPlanAsync(AkeneoProductSyncContext context,
        CancellationToken cancellationToken)
    {
        var plan = new AkeneoExecutableSectionPlan();
        foreach (var group in context.GetMappings(NopTargetType.CustomHandler)
                     .GroupBy(m => m.Mapping.NopTargetKey?.Trim() ?? "", StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var mapped = group.First();
            var handler = registry.Find(group.Key);
            void Review(string message) => plan.AddReview("Custom mappings",
                handler?.DisplayName ?? "Handler unavailable: " + group.Key, mapped.Mapping.AkeneoAttributeCode,
                message, group.Any(m => m.Mapping.IsRequired));
            if (handler == null)
            {
                Review("Custom mapping handler '" + group.Key + "' is unavailable. This mapping made no changes.");
                continue;
            }
            if (group.Count() != 1)
            {
                Review("Multiple mappings select '" + handler.DisplayName + "' for this product. Resolve the conflict before syncing.");
                continue;
            }
            var mapping = mapped.Mapping;
            if (mapping.ValueMode != AkeneoAttributeMappingValueMode.SingleAttribute ||
                !string.IsNullOrWhiteSpace(mapping.TransformRuleJson))
            {
                Review("Custom handlers require a direct, untransformed Akeneo attribute value.");
                continue;
            }
            var code = mapping.AkeneoAttributeCode;
            if (string.IsNullOrWhiteSpace(code))
            {
                Review("The custom mapping has no source attribute.");
                continue;
            }
            if (!_attributes.TryGetValue(code, out var attribute))
            {
                attribute = await api.GetAttributeByCodeAsync(code, cancellationToken);
                _attributes[code] = attribute;
            }
            if (attribute == null || !handler.SupportedAkeneoTypes.Contains(attribute.Type, StringComparer.OrdinalIgnoreCase))
            {
                Review("The source attribute is missing or its type is incompatible with " + handler.DisplayName + ".");
                continue;
            }
            var errors = handler.Validate(mapping, attribute).ToList();
            if (errors.Count > 0)
            {
                foreach (var error in errors) Review(error);
                continue;
            }
            var handlerPlan = new AkeneoExecutableSectionPlan();
            await handler.PlanAsync(new AkeneoCustomMappingContext
            {
                ProductContext = context, MappedValue = mapped, Attribute = attribute
            }, handlerPlan, cancellationToken);
            // A review invalidates the handler's entire proposed replacement.
            var hasReview = handlerPlan.Operations.Any(o => o.Preview?.ChangeType == AkeneoDryRunOperationType.Review);
            foreach (var operation in handlerPlan.Operations)
                if (!hasReview || operation.Preview?.ChangeType == AkeneoDryRunOperationType.Review)
                    plan.Operations.Add(operation);
            foreach (var warning in handlerPlan.Warnings) plan.Warnings.Add(warning);
            foreach (var message in handlerPlan.Messages) plan.Messages.Add(message);
        }
        return plan;
    }
}
