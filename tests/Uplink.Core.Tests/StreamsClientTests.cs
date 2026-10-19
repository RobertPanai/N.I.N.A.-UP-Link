using System.Net;
using MyUniverseUplink.Core.Model;
using MyUniverseUplink.Core.Transport;
using MyUniverseUplink.Core.Tests.Support;
using Xunit;

namespace MyUniverseUplink.Core.Tests;

public class StreamsClientTests
{
    private const string Token = "tok-SECRET-123";
    private const string Ack = """
        {"schemaVersion":1,"requestId":"R1","results":[{"streamId":"cam-cielo","status":"stored"}],"stored":1,"rejected":0,"removed":0,"visible":true}
        """;

    [Fact]
    public async Task PutStreamsSendsGzipBodyWithBearerAndDevice()
    {
        var handler = new FakeHttpHandler((_, _) => Task.FromResult(FakeHttpHandler.Json(HttpStatusCode.OK, Ack)));
        var client = new UplinkClient(new HttpClient(handler), Options());
        var request = new StreamsRequest
        {
            RequestId = "R1",
            Device = new DeviceInfo { DeviceId = "osservatorio-casa" },
            StreamsEnabled = true,
            Streams = [new StreamEntry { StreamId = "cam-cielo", Title = "Cielo", Url = "https://astro.example.org/x", Order = 0 }],
        };

        var result = await client.PutStreamsAsync(request, CancellationToken.None);

        var captured = Assert.Single(handler.Requests);
        Assert.Equal("PUT", captured.Method);
        Assert.Equal("https://robertpanai.com/api/uplink/streams", captured.Url);
        Assert.Equal("gzip", captured.ContentEncoding);
        Assert.Equal("Bearer " + Token, captured.Authorization);
        Assert.Equal("osservatorio-casa", captured.DeviceHeader);
        Assert.Contains("\"streamsEnabled\":true", captured.DecompressedBody());
        Assert.Equal(200, result.StatusCode);
        Assert.Equal<bool?>(true, result.Body?.Visible);
        Assert.Equal(1, result.Body?.Stored);
    }

    [Fact]
    public async Task UnauthorizedStreamsPutIsClassifiedAsAuthError()
    {
        var handler = new FakeHttpHandler((_, _) => Task.FromResult(FakeHttpHandler.Json(HttpStatusCode.Unauthorized, "{}")));
        var client = new UplinkClient(new HttpClient(handler), Options());

        var result = await client.PutStreamsAsync(new StreamsRequest { RequestId = "R1", Device = new DeviceInfo { DeviceId = "d" } }, CancellationToken.None);

        Assert.Equal(SendOutcome.AuthError, ErrorClassifier.Classify(result).Outcome);
    }

    private static UplinkClientOptions Options() => new()
    {
        BaseUrl = new Uri("https://robertpanai.com"),
        DeviceId = "osservatorio-casa",
        BearerToken = Token,
    };
}
