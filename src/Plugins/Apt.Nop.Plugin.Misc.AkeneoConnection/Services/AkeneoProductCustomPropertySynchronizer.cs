using System.Text.Json;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;
using Nop.Services.Common;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public class AkeneoProductCustomPropertySynchronizer(
 IGenericAttributeService genericAttributeService)
 : IAkeneoProductSectionSynchronizer
{
    public int Order => 600;

    public async Task SynchronizeAsync(
        AkeneoProductSyncContext context,
        CancellationToken cancellationToken = default)
    {
        if (context.Product is null)
            return;

        var changed = false;

        foreach (var mapped in context.GetMappings(NopTargetType.CustomProperty))
        {
            var key = mapped.Mapping.NopTargetKey?.Trim();

            if (string.IsNullOrWhiteSpace(key))
            {
                var attributeCode = mapped.Mapping.AkeneoAttributeCode?.Trim();
                key = string.IsNullOrWhiteSpace(attributeCode)
                    ? null
                    : $"Apt.Akeneo.CustomProperty.{attributeCode}";
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                context.Result.AddWarning(
                    $"Custom property mapping has no target key. Akeneo attribute: {mapped.Mapping.AkeneoAttributeCode}");
                continue;
            }

            var existing = await genericAttributeService
                .GetAttributeAsync<string>(context.Product, key) ?? string.Empty;

            if (!mapped.HasValue)
            {
                if (context.Request.CustomPropertyMissingValueBehavior ==
                    AkeneoMissingValueBehavior.PreserveExisting)
                {
                    continue;
                }

                if (existing.Length == 0)
                    continue;

                // Saving null deletes the generic attribute record.
                await genericAttributeService.SaveAttributeAsync<string>(
                    context.Product, key, null);

                context.Result.AddMessage($"Cleared custom property '{key}'.");
                changed = true;
                continue;
            }

            var desired = mapped.Value?.DisplayValues is { Count: > 1 } values
                ? JsonSerializer.Serialize(values)
                : mapped.DisplayValue?.Trim() ?? string.Empty;

            if (string.Equals(existing, desired, StringComparison.Ordinal))
                continue;

            await genericAttributeService.SaveAttributeAsync(
                context.Product, key, desired);

            context.Result.AddMessage($"Set custom property '{key}' for product ID {context.Product.Id}.");
            changed = true;
        }

        if (changed)
            context.MarkChanged();
    }
}

