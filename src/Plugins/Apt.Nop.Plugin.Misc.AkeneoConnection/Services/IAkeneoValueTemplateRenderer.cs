using Apt.Nop.Plugin.Misc.AkeneoConnection.Types;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

public interface IAkeneoValueTemplateRenderer
{
    AkeneoValueTemplateValidationResult Validate(string template);

    AkeneoValueTemplateRenderResult Render(
        string template,
        AkeneoValueTemplateContext context);
}
