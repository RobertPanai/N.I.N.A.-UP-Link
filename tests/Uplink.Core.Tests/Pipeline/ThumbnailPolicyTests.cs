using MyUniverseUplink.Core.Pipeline;
using MyUniverseUplink.Core.Settings;
using Xunit;

namespace MyUniverseUplink.Core.Tests.Pipeline;

public class ThumbnailPolicyTests
{
    private static readonly DateTimeOffset Night = new(2026, 1, 2, 22, 0, 0, TimeSpan.Zero); // notte 2026-01-02

    private static ThumbnailContext At(DateTimeOffset when, string target = "WitchHead", string filter = "Ha", bool force = false)
        => new(target, filter, when, force);

    [Fact]
    public void NeverNeverSendsEvenWhenForced()
    {
        var policy = new ThumbnailPolicy(ThumbnailMode.Never);
        Assert.False(policy.ShouldSend(At(Night)));
        Assert.False(policy.ShouldSend(At(Night, force: true)));
    }

    [Fact]
    public void AlwaysSendsEveryFrame()
    {
        var policy = new ThumbnailPolicy(ThumbnailMode.Always, maxPerNight: 1);
        Assert.True(policy.ShouldSend(At(Night)));
        Assert.True(policy.ShouldSend(At(Night.AddMinutes(1))));
        Assert.True(policy.ShouldSend(At(Night.AddMinutes(2))));
    }

    [Fact]
    public void SampledSendsFirstPerTargetAndFilterThenEveryFifteenMinutes()
    {
        var policy = new ThumbnailPolicy(ThumbnailMode.Sampled);

        Assert.True(policy.ShouldSend(At(Night)));
        Assert.False(policy.ShouldSend(At(Night.AddMinutes(5))));
        Assert.False(policy.ShouldSend(At(Night.AddMinutes(14))));
        Assert.True(policy.ShouldSend(At(Night.AddMinutes(15))));
        Assert.False(policy.ShouldSend(At(Night.AddMinutes(20))));
    }

    [Fact]
    public void ANewFilterOfTheSameTargetIsSentAtOnce()
    {
        var policy = new ThumbnailPolicy(ThumbnailMode.Sampled);

        Assert.True(policy.ShouldSend(At(Night, filter: "Ha")));
        Assert.False(policy.ShouldSend(At(Night.AddMinutes(1), filter: "Ha")));
        Assert.True(policy.ShouldSend(At(Night.AddMinutes(2), filter: "OIII")));
    }

    [Fact]
    public void NightChangeResetsTheFirstOfKindRule()
    {
        var policy = new ThumbnailPolicy(ThumbnailMode.Sampled, sampleInterval: TimeSpan.FromHours(24));

        Assert.True(policy.ShouldSend(At(Night)));
        Assert.False(policy.ShouldSend(At(Night.AddHours(1))));

        // Nuova notte (locale Roma - 12 h cambia data): stesso target, prima del nuovo intervallo.
        Assert.True(policy.ShouldSend(At(new DateTimeOffset(2026, 1, 3, 12, 0, 0, TimeSpan.Zero))));
    }

    [Fact]
    public void NightCapStopsSamplingUntilTheNextNight()
    {
        var policy = new ThumbnailPolicy(ThumbnailMode.Sampled, maxPerNight: 2);

        Assert.True(policy.ShouldSend(At(Night, target: "A")));
        Assert.True(policy.ShouldSend(At(Night, target: "B")));
        Assert.False(policy.ShouldSend(At(Night, target: "C")));
    }

    [Fact]
    public void ForceBypassesSamplingAndTheCap()
    {
        var policy = new ThumbnailPolicy(ThumbnailMode.Sampled, maxPerNight: 1);

        Assert.True(policy.ShouldSend(At(Night, target: "A")));
        Assert.False(policy.ShouldSend(At(Night, target: "B")));
        Assert.True(policy.ShouldSend(At(Night.AddMinutes(1), target: "A", force: true)));
    }

    [Fact]
    public void ConstructorRejectsANonPositiveCap()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ThumbnailPolicy(ThumbnailMode.Sampled, maxPerNight: 0));
    }
}
