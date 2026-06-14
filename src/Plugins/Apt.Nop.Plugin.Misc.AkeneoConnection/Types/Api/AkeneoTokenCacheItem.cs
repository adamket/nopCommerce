namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types.Api;
public class AkeneoTokenCacheItem
{
    public string AccessToken { get; set; } = string.Empty;

    public string RefreshToken { get; set; } = string.Empty;

    public DateTime AccessTokenExpiresOnUtc { get; set; }
}
