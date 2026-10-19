using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using MyUniverseUplink.Core.Abstractions;
using MyUniverseUplink.Core.Model;
using MyUniverseUplink.Core.Transport;

namespace MyUniverseUplink.Core.Tests.Support;

internal sealed class FakeClock : IClock
{
    public FakeClock(DateTimeOffset now) => UtcNow = now;

    public DateTimeOffset UtcNow { get; set; }
}

internal sealed class FixedJitter(double value) : IJitter
{
    public double NextDouble() => value;
}

/// <summary>Trasporto finto: registra i batch ricevuti e risponde con una funzione (batch, indice chiamata).</summary>
internal sealed class FakeTransport(Func<FrameBatch, int, UplinkResponse<BatchResponse>> respond) : IUplinkTransport
{
    private readonly object _gate = new();
    private readonly List<FrameBatch> _batches = new();

    public Func<FrameBatch, int, UplinkResponse<BatchResponse>> Respond { get; set; } = respond;

    public IReadOnlyList<FrameBatch> Batches
    {
        get
        {
            lock (_gate)
            {
                return _batches.ToList();
            }
        }
    }

    public int Calls => Batches.Count;

    public Task<UplinkResponse<BatchResponse>> PostFramesAsync(FrameBatch batch, CancellationToken cancellationToken)
    {
        int index;
        lock (_gate)
        {
            index = _batches.Count;
            _batches.Add(batch);
        }

        return Task.FromResult(Respond(batch, index));
    }

    public static UplinkResponse<BatchResponse> Ack(FrameBatch batch, string status = FrameStatus.Stored) =>
        new(200, new BatchResponse
        {
            BatchId = batch.BatchId,
            Results = batch.Frames.Select(f => new FrameResult { FrameId = f.FrameId, Status = status }).ToList(),
        }, null, TransportFailure.None, null);

    public static UplinkResponse<BatchResponse> Http(int status, TimeSpan? retryAfter = null) =>
        new(status, null, retryAfter, TransportFailure.None, null);
}

internal sealed record CapturedRequest(
    string Method,
    string Url,
    string? Authorization,
    string? DeviceHeader,
    string? UserAgent,
    string? ExtraHeader,
    string? ContentEncoding,
    byte[] Body)
{
    public string DecompressedBody()
    {
        using var gzip = new GZipStream(new MemoryStream(Body), CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return Encoding.UTF8.GetString(output.ToArray());
    }
}

/// <summary>HttpMessageHandler finto: registra le richieste e risponde con una funzione.</summary>
internal sealed class FakeHttpHandler(Func<CapturedRequest, CancellationToken, Task<HttpResponseMessage>> respond)
    : HttpMessageHandler
{
    private readonly List<CapturedRequest> _requests = new();
    private readonly object _gate = new();

    public IReadOnlyList<CapturedRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return _requests.ToList();
            }
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? Array.Empty<byte>()
            : await request.Content.ReadAsByteArrayAsync(cancellationToken);

        string? Header(string name) =>
            request.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;

        var captured = new CapturedRequest(
            request.Method.Method,
            request.RequestUri!.ToString(),
            request.Headers.Authorization?.ToString(),
            Header("X-Uplink-Device"),
            Header("User-Agent"),
            Header("x-vercel-protection-bypass"),
            request.Content?.Headers.ContentEncoding.FirstOrDefault(),
            body);

        lock (_gate)
        {
            _requests.Add(captured);
        }

        return await respond(captured, cancellationToken);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json, TimeSpan? retryAfter = null)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        if (retryAfter is { } delay)
        {
            response.Headers.RetryAfter = new RetryConditionHeaderValue(delay);
        }

        return response;
    }
}

internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "uplink-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
