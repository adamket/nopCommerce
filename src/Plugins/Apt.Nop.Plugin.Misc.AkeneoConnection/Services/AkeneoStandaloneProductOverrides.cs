using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Sync;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

internal static class AkeneoStandaloneProductOverrides
{
    internal static async Task ApplyAsync(
        AkeneoProductSyncContext context,
        AkeneoFamilySubModelRule rule,
        IAkeneoValueTemplateRenderer renderer,
        CancellationToken cancellationToken = default)
    {
        if (rule == null || rule.UsesSubModelParent() ||
            rule.VariantRelationshipOverrideMode != AkeneoVariantRelationshipMode.None)
            return;

        context.StandaloneProductTemplateId = rule.StandaloneProductTemplateId;
        if (string.IsNullOrWhiteSpace(rule.StandaloneProductNameTemplate))
            return;

        if (renderer == null)
            throw new InvalidOperationException("The standalone product name template renderer is unavailable.");

        var rendered = await renderer.RenderAsync(rule.StandaloneProductNameTemplate, new AkeneoValueTemplateContext
        {
            Source = context.Source,
            Locale = context.Request.Locale,
            Channel = context.Request.Channel,
            Currency = context.Request.Currency,
            FamilyCode = context.MappingFamilyCode,
            FamilyVariantCode = context.MappingFamilyVariantCode,
            Sku = context.Sku
        }, cancellationToken);

        if (rendered.Errors.Count > 0)
            throw new InvalidOperationException("Standalone product name template: " + string.Join(" ", rendered.Errors));

        // Missing tokens and blank results retain the ordinary product-name mapping.
        context.StandaloneProductNameOverride = rendered.Success && !string.IsNullOrWhiteSpace(rendered.Value)
            ? rendered.Value.Trim() : null;
    }
}
