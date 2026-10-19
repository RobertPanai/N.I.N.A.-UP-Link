using System.Net;
using MyUniverseUplink.Core.Model;
using MyUniverseUplink.Core.Transport;
using MyUniverseUplink.Core.Tests.Support;
using Xunit;

namespace MyUniverseUplink.Core.Tests.Transport;

public class UplinkClientTests
{
    private const string Token = "tok-SECRET-123";
    private const string Ack = """
        {"schemaVersion":1,"batchId":"B1","results":[{"frameId":"f1","status":"stored"}],"accepted":1,"duplicates":0,"rejected":0}
        """;

    private static FrameBatch SampleBatch() => new()
    {
        BatchId = "B1",
        Device = new DeviceInfo { DeviceId = "osservatorio-casa" },
        Frames = [new FrameRecord { FrameId = "f1", CapturedAtUtc = DateTimeOffset.UtcNow }],
    };

    private static UplinkClientOptions Options(TimeSpan? timeout = null, string? extraName = null, string? extraValue = null) => new()
    {
        BaseUrl = new Uri("https://robertpanai.com"),
        DeviceId = "osservatorio-casa",
        BearerToken = Token,
        ExtraHeaderName = extraName,
        ExtraHeaderValue = extraValue,
        Timeout = timeout ?? TimeSpan.FromSeconds(30),
    };

    [Fact]
    public async Task PostSendsGzipBodyBearerDeviceAndExtraHeader()
    {
        var handler = new FakeHttpHandler((_, _) =>
            Task.FromResult(FakeHttpHandler.Json(HttpStatusCode.OK, Ack)));
        var client = new UplinkClient(new HttpClient(handler), Options(extraName: "x-vercel-protection-bypass", extraValue: "bypass-value"));

        var result = await client.PostFramesAsync(SampleBatch(), CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal("https://robertpanai.com/api/uplink/frames", request.Url);
        Assert.Equal("gzip", request.ContentEncoding);
        Assert.Equal("Bearer " + Token, request.Authorization);
        Assert.Equal("osservatorio-casa", request.DeviceHeader);
        Assert.Equal("bypass-value", request.ExtraHeader);
        Assert.Contains("MyUniverseUplink", request.UserAgent);
        Assert.Contains("\"batchId\":\"B1\"", request.DecompressedBody());

        Assert.Equal(200, result.StatusCode);
        Assert.Equal("B1", result.Body?.BatchId);
        Assert.Equal(FrameStatus.Stored, result.Body?.Results[0].Status);
    }

    [Fact]
    public async Task HealthUsesGetOnTheHealthPath()
    {
        const string health = """{"schemaVersion":1,"serverVersion":"0.1.0","maxFramesPerBatch":25,"authenticated":true,"deviceId":"osservatorio-casa"}""";
        var handler = new FakeHttpHandler((_, _) => Task.FromResult(FakeHttpHandler.Json(HttpStatusCode.OK, health)));
        var client = new UplinkClient(new HttpClient(handler), Options());

        var result = await client.GetHealthAsync(CancellationToken.None);

        Assert.Equal("GET", handler.Requests[0].Method);
        Assert.Equal("https://robertpanai.com/api/uplink/health", handler.Requests[0].Url);
        Assert.Equal(25, result.Body?.MaxFramesPerBatch);
        Assert.Null(handler.Requests[0].ExtraHeader);
    }

    [Fact]
    public async Task RetryAfterSecondsAreParsed()
    {
        var handler = new FakeHttpHandler((_, _) =>
            Task.FromResult(FakeHttpHandler.Json(HttpStatusCode.TooManyRequests, "{}", TimeSpan.FromSeconds(7))));
        var client = new UplinkClient(new HttpClient(handler), Options());

        var result = await client.PostFramesAsync(SampleBatch(), CancellationToken.None);

        Assert.Equal(429, result.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(7), result.RetryAfter);
        Assert.Null(result.Body);
    }

    [Fact]
    public async Task SlowServerBecomesATimeoutFailure()
    {
        var handler = new FakeHttpHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return FakeHttpHandler.Json(HttpStatusCode.OK, Ack);
        });
        var client = new UplinkClient(new HttpClient(handler), Options(timeout: TimeSpan.FromMilliseconds(200)));

        var result = await client.PostFramesAsync(SampleBatch(), CancellationToken.None);

        Assert.Null(result.StatusCode);
        Assert.Equal(TransportFailure.Timeout, result.Failure);
    }

    [Fact]
    public async Task NetworkErrorBecomesANetworkFailure()
    {
        var handler = new FakeHttpHandler((_, _) => throw new HttpRequestException("connection reset"));
        var client = new UplinkClient(new HttpClient(handler), Options());

        var result = await client.PostFramesAsync(SampleBatch(), CancellationToken.None);

        Assert.Equal(TransportFailure.Network, result.Failure);
        Assert.Null(result.StatusCode);
    }

    [Fact]
    public async Task CallerCancellationPropagatesInsteadOfBeingReported()
    {
        using var cts = new CancellationTokenSource();
        var handler = new FakeHttpHandler(async (_, ct) =>
        {
            cts.Cancel();
            await Task.Delay(Timeout.Infinite, ct);
            return FakeHttpHandler.Json(HttpStatusCode.OK, Ack);
        });
        var client = new UplinkClient(new HttpClient(handler), Options());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.PostFramesAsync(SampleBatch(), cts.Token));
    }

    [Fact]
    public void PlainHttpToARemoteHostIsRefused()
    {
        var options = Options();
        var insecure = new UplinkClientOptions
        {
            BaseUrl = new Uri("http://robertpanai.com"),
            DeviceId = "d",
            BearerToken = Token,
        };

        Assert.Throws<ArgumentException>(() => new UplinkClient(new HttpClient(new FakeHttpHandler((_, _) => throw new InvalidOperationException())), insecure));
        Assert.NotNull(new UplinkClient(new HttpClient(new FakeHttpHandler((_, _) => throw new InvalidOperationException())), options));
    }

    [Fact]
    public void OptionsToStringNeverShowsTheToken()
    {
        Assert.DoesNotContain(Token, Options().ToString());
    }
}
