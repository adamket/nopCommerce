namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Helpers;

public static class AkeneoUtc
{
    /// <summary>
    /// A time the plugin stores as UTC, as UTC. Times read back from the
    /// database have no kind, and ToUniversalTime() would treat them as server
    /// local time and shift them by the server's offset (5 hours on a US
    /// Central machine), pushing delta filters into the future.
    /// </summary>
    public static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Local => value.ToUniversalTime(),
        DateTimeKind.Utc => value,
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
