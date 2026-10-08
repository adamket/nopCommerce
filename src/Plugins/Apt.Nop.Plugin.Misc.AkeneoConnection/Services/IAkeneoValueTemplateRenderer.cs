using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public interface IAkeneoValueTemplateRenderer
{
    AkeneoValueTemplateValidationResult Validate(string template);

    /// <summary>
    /// Renders synchronously. <c>{attribute.field}</c> reference-entity tokens
    /// only resolve when their values are already in
    /// <see cref="AkeneoValueTemplateContext.ReferenceFieldValues"/>.
    /// </summary>
    AkeneoValueTemplateRenderResult Render(
        string template,
        AkeneoValueTemplateContext context);

    /// <summary>
    /// Looks up the reference-entity records behind any <c>{attribute.field}</c>
    /// tokens, then renders. Use this wherever such tokens may appear.
    /// </summary>
    Task<AkeneoValueTemplateRenderResult> RenderAsync(
        string template,
        AkeneoValueTemplateContext context,
        CancellationToken cancellationToken = default);
}
