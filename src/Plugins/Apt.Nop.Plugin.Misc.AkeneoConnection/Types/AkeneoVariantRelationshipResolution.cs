using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types;
public class AkeneoVariantRelationshipResolution
{
    public AkeneoVariantRelationshipMode Mode { get; set; }

    public AkeneoVariantRelationshipSource Source { get; set; }

    public AkeneoVariantRelationshipOptions Options { get; set; }
}
