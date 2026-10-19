using MyUniverseUplink.Core.Model;
using MyUniverseUplink.Core.Transport;
using Xunit;

namespace MyUniverseUplink.Core.Tests.Transport;

public class ErrorClassifierTests
{
    private static UplinkResponse<BatchResponse> Http(int? status, BatchResponse? body = null, TimeSpan? retryAfter = null) =>
        new(status, body, retryAfter, TransportFailure.None, null);

    private static readonly BatchResponse AnyBody = new() { BatchId = "b" };

    [Theory]
    [InlineData(401, SendOutcome.AuthError)]
    [InlineData(403, SendOutcome.AuthError)]
    [InlineData(413, SendOutcome.PayloadTooLarge)]
    [InlineData(429, SendOutcome.RateLimited)]
    [InlineData(408, SendOutcome.Transient)]
    [InlineData(500, SendOutcome.Transient)]
    [InlineData(502, SendOutcome.Transient)]
    [InlineData(503, SendOutcome.Transient)]
    [InlineData(504, SendOutcome.Transient)]
    [InlineData(302, SendOutcome.Transient)]
    [InlineData(400, SendOutcome.Permanent)]
    [InlineData(404, SendOutcome.Permanent)]
    [InlineData(415, SendOutcome.Permanent)]
    [InlineData(422, SendOutcome.Permanent)]
    public void HttpStatusMapsToOutcome(int status, SendOutcome expected)
    {
        Assert.Equal(expected, ErrorClassifier.Classify(Http(status)).Outcome);
    }

    [Fact]
    public void TwoHundredWithBodyIsAcknowledged()
    {
        Assert.Equal(SendOutcome.Acknowledged, ErrorClassifier.Classify(Http(200, AnyBody)).Outcome);
    }

    [Fact]
    public void TwoHundredWithUnreadableBodyIsTransient()
    {
        Assert.Equal(SendOutcome.Transient, ErrorClassifier.Classify(Http(200)).Outcome);
    }

    [Fact]
    public void TimeoutNetworkAndMissingResponseAreTransient()
    {
        Assert.Equal(SendOutcome.Transient,
            ErrorClassifier.Classify(new UplinkResponse<BatchResponse>(null, null, null, TransportFailure.Timeout, "x")).Outcome);
        Assert.Equal(SendOutcome.Transient,
            ErrorClassifier.Classify(new UplinkResponse<BatchResponse>(null, null, null, TransportFailure.Network, "dns")).Outcome);
        Assert.Equal(SendOutcome.Transient, ErrorClassifier.Classify(Http(null)).Outcome);
    }

    [Fact]
    public void RetryAfterIsCarriedByRateLimitedOutcome()
    {
        var classification = ErrorClassifier.Classify(Http(429, retryAfter: TimeSpan.FromSeconds(42)));
        Assert.Equal(TimeSpan.FromSeconds(42), classification.RetryAfter);
    }

    [Fact]
    public void ReasonNeverContainsTheBody()
    {
        var body = new BatchResponse { BatchId = "secret-looking-body" };
        var classification = ErrorClassifier.Classify(Http(500, body));
        Assert.DoesNotContain("secret-looking-body", classification.Reason);
    }
}
