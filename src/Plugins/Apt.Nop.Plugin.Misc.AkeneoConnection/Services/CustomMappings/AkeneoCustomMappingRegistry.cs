using Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api.Dto;
namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Services.CustomMappings;

public sealed class AkeneoCustomMappingRegistry
{
    private readonly Dictionary<string, IAkeneoCustomMappingHandler> _handlers = new(StringComparer.OrdinalIgnoreCase);
    public AkeneoCustomMappingRegistry(IEnumerable<IAkeneoCustomMappingHandler> handlers)
    {
        foreach (var handler in handlers)
            if (string.IsNullOrWhiteSpace(handler.Key) || handler.Key != handler.Key.Trim() ||
                !_handlers.TryAdd(handler.Key, handler))
                throw new InvalidOperationException("Custom handler keys must be nonempty and unique: " + handler.Key);
    }
    public IAkeneoCustomMappingHandler Find(string key) =>
        key != null && _handlers.TryGetValue(key.Trim(), out var handler) ? handler : null;
    public IEnumerable<IAkeneoCustomMappingHandler> All => _handlers.Values;
    public IEnumerable<IAkeneoCustomMappingHandler> GetCompatible(AkeneoAttributeDefinition attribute) =>
        _handlers.Values.Where(h => h.SupportedAkeneoTypes.Contains(attribute?.Type, StringComparer.OrdinalIgnoreCase))
            .OrderBy(h => h.DisplayName);
}
