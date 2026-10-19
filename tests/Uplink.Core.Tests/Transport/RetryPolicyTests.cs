using MyUniverseUplink.Core.Tests.Support;
using MyUniverseUplink.Core.Transport;
using Xunit;

namespace MyUniverseUplink.Core.Tests.Transport;

public class RetryPolicyTests
{
    [Fact]
    public void DelayGrowsExponentiallyWithNeutralJitter()
    {
        // jitter 0 -> fattore 0,5: il ritardo e' meta' del backoff puro.
        var policy = new RetryPolicy(new FixedJitter(0.0));

        Assert.Equal(TimeSpan.FromSeconds(2.5), policy.GetDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(5), policy.GetDelay(2));
        Assert.Equal(TimeSpan.FromSeconds(10), policy.GetDelay(3));
        Assert.Equal(TimeSpan.FromSeconds(20), policy.GetDelay(4));
    }

    [Fact]
    public void DelayIsCappedAtFifteenMinutes()
    {
        var policy = new RetryPolicy(new FixedJitter(0.0));
        Assert.Equal(TimeSpan.FromMinutes(7.5), policy.GetDelay(30));

        var maxJitter = new RetryPolicy(new FixedJitter(0.999999));
        Assert.True(maxJitter.GetDelay(30) <= TimeSpan.FromMinutes(15));
        Assert.True(maxJitter.GetDelay(30) > TimeSpan.FromMinutes(14));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(0.999)]
    public void JitterStaysInsideTheExpectedBand(double random)
    {
        var policy = new RetryPolicy(new FixedJitter(random));
        var raw = TimeSpan.FromSeconds(5 * 4); // tentativo 3: 5 * 2^2 = 20 s

        var delay = policy.GetDelay(3);

        Assert.True(delay >= raw * 0.5 && delay < raw);
    }

    [Fact]
    public void RetryAfterIsAMinimumAndIsHonouredBeyondTheCap()
    {
        var policy = new RetryPolicy(new FixedJitter(0.0));

        Assert.Equal(TimeSpan.FromSeconds(60), policy.GetDelay(1, TimeSpan.FromSeconds(60)));
        Assert.Equal(TimeSpan.FromHours(1), policy.GetDelay(1, TimeSpan.FromHours(1)));
    }

    [Fact]
    public void ShorterRetryAfterDoesNotShortenTheBackoff()
    {
        var policy = new RetryPolicy(new FixedJitter(0.0));
        Assert.Equal(TimeSpan.FromSeconds(10), policy.GetDelay(3, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void DefaultsMatchThePlan()
    {
        var policy = new RetryPolicy(new FixedJitter(0.5));
        Assert.Equal(TimeSpan.FromSeconds(5), policy.BaseDelay);
        Assert.Equal(TimeSpan.FromMinutes(15), policy.MaxDelay);
        Assert.Equal(8, policy.MaxAttempts);
    }
}
