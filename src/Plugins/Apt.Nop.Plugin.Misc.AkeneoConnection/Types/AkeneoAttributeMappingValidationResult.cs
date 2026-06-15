namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
public class AkeneoAttributeMappingValidationResult
{
    public bool Success => !Errors.Any();

    public IList<string> Errors { get; set; } = new List<string>();
}