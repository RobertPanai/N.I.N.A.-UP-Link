using MyUniverseUplink.Core.Identity;
using Xunit;

namespace MyUniverseUplink.Core.Tests;

public class IdentityTests
{
    private static readonly TimeZoneInfo Rome = TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");

    [Theory]
    [InlineData("2025-11-07T23:30:00Z", "2025-11-07")]
    [InlineData("2025-11-07T00:30:00Z", "2025-11-06")]
    [InlineData("2025-11-06T23:30:00Z", "2025-11-06")]
    [InlineData("2025-07-01T21:30:00Z", "2025-07-01")]
    [InlineData("2025-07-01T09:00:00Z", "2025-06-30")]
    public void NightIdUsesRomeLocalTimeMinusTwelveHours(string utc, string expectedNight)
    {
        var captured = DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(expectedNight, NightId.For(captured, Rome));
    }

    [Fact]
    public void NightIdCrossesDstWithoutShift()
    {
        // Passaggio all'ora solare il 26/10/2025: la notte resta la stessa per entrambi gli istanti.
        var beforeSwitch = DateTimeOffset.Parse("2025-10-25T23:30:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var afterSwitch = DateTimeOffset.Parse("2025-10-26T23:30:00Z", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal("2025-10-25", NightId.For(beforeSwitch, Rome));
        Assert.Equal("2025-10-26", NightId.For(afterSwitch, Rome));
    }

    [Fact]
    public void UlidHasTwentySixCrockfordCharactersAndSortsByTime()
    {
        const string alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        var early = UlidFactory.NewUlid(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var late = UlidFactory.NewUlid(new DateTimeOffset(2026, 1, 1, 0, 0, 1, TimeSpan.Zero));

        Assert.Equal(26, early.Length);
        Assert.All(early, c => Assert.Contains(c, alphabet));
        Assert.True(string.CompareOrdinal(early, late) < 0);
        Assert.NotEqual(early, UlidFactory.NewUlid(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
    }
}
