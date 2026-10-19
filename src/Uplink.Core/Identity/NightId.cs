using System.Globalization;

namespace MyUniverseUplink.Core.Identity;

/// <summary>
/// Notte astronomica: data locale (Europe/Rome) dell'istante meno 12 ore.
/// Le riprese dopo mezzanotte appartengono alla notte iniziata la sera prima.
/// </summary>
public static class NightId
{
    public const string RomeTimeZoneId = "Europe/Rome";

    private static readonly Lazy<TimeZoneInfo> RomeZone =
        new(() => TimeZoneInfo.FindSystemTimeZoneById(RomeTimeZoneId));

    public static string For(DateTimeOffset capturedAtUtc) => For(capturedAtUtc, RomeZone.Value);

    public static string For(DateTimeOffset capturedAtUtc, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(capturedAtUtc, zone);
        return local.AddHours(-12).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
