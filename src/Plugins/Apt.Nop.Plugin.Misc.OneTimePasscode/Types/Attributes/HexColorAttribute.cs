using System.ComponentModel.DataAnnotations;

namespace Apt.Nop.Plugin.Misc.OneTimePasscode.Types.Attributes;


public class HexColorAttribute : RegularExpressionAttribute
{
    public HexColorAttribute() : base("^#([0-9A-Fa-f]{6})$")
    {
        ErrorMessage = "Please enter a valid 6-digit hex color.";
    }
}
