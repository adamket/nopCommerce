
using System.ComponentModel.DataAnnotations;

namespace Apt.Nop.Plugin.Misc.OneTimePasscode.Models;
public class OtpLoginModel
{
    [Required]
    [EmailAddress]
    public string Email { get; set; }
    public string Otp { get; set; }
    public int CodeExpiryMinutes { get; set; }

    public string RedirectUrlPath { get; set; }

    public int CodeDigitCount { get; set; }
}
