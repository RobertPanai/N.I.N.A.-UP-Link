using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;
using MyUniverseUplink.Core.Model;
using MyUniverseUplink.Core.Settings;

namespace MyUniverseUplink.Core.Transport;

/// <summary>Invio di batch, dirette e health check. Il token non compare mai in ToString, in log o in eccezioni.</summary>
public interface IUplinkTransport
{
    Task<UplinkResponse<BatchResponse>> PostFramesAsync(FrameBatch batch, CancellationToken cancellationToken);
}

public sealed class UplinkClientOptions
{
    public required Uri BaseUrl { get; init; }

    public required string DeviceId { get; init; }

    /// <summary>Segreto: usato solo nell'header Authorization.</summary>
    public required string BearerToken { get; init; }

    public string? ExtraHeaderName { get; init; }

    /// <summary>Segreto: mai in log.</summary>
    public string? ExtraHeaderValue { get; init; }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    public string UserAgent { get; init; } = "MyUniverseUplink/0.0 (Core)";

    public override string ToString() => $"UplinkClientOptions(BaseUrl={BaseUrl}, DeviceId={DeviceId})";
}

public enum TransportFailure
{
    None,
    Timeout,
    Network,
}

/// <summary>
/// Esito grezzo di una chiamata HTTP. StatusCode nullo = nessuna risposta (timeout o rete).
/// Body valorizzato solo per 2xx letti correttamente.
/// </summary>
public sealed record UplinkResponse<T>(
    int? StatusCode,
    T? Body,
    TimeSpan? RetryAfter,
    TransportFailure Failure,
    string? ErrorMessage) where T : class;

public sealed class UplinkClient : IUplinkTransport, IStreamsTransport
{
    public const string FramesPath = "api/uplink/frames";
    public const string HealthPath = "api/uplink/health";
    public const string StreamsPath = "api/uplink/streams";

    private readonly HttpClient _http;
    private readonly UplinkClientOptions _options;
    private readonly Uri _baseUri;

    /// <param name="http">HttpClient da riusare per tutta la vita del plugin (il chiamante sceglie l'handler).</param>
    public UplinkClient(HttpClient http, UplinkClientOptions options)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (!SettingsValidator.IsAllowedServerUri(options.BaseUrl))
        {
            throw new ArgumentException("Server URL non consentito: serve https (http solo verso localhost).", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.BearerToken))
        {
            throw new ArgumentException("Token mancante.", nameof(options));
        }

        _baseUri = new Uri(options.BaseUrl.GetLeftPart(UriPartial.Authority) + options.BaseUrl.AbsolutePath.TrimEnd('/') + "/");
    }

    public Task<UplinkResponse<BatchResponse>> PostFramesAsync(FrameBatch batch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        return SendAsync<BatchResponse>(HttpMethod.Post, FramesPath, GzipJsonContent(batch), cancellationToken);
    }

    /// <summary>PUT dell'insieme delle dirette: sostituisce tutto quello che il sito ha per questo dispositivo.</summary>
    public Task<UplinkResponse<StreamsResponse>> PutStreamsAsync(StreamsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendAsync<StreamsResponse>(HttpMethod.Put, StreamsPath, GzipJsonContent(request), cancellationToken);
    }

    public Task<UplinkResponse<HealthResponse>> GetHealthAsync(CancellationToken cancellationToken)
        => SendAsync<HealthResponse>(HttpMethod.Get, HealthPath, null, cancellationToken);

    private static ByteArrayContent GzipJsonContent<T>(T value)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(value, UplinkJson.Options);
        byte[] compressed;
        using (var buffer = new MemoryStream())
        {
            using (var gzip = new GZipStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
            {
                gzip.Write(json, 0, json.Length);
            }

            compressed = buffer.ToArray();
        }

        var content = new ByteArrayContent(compressed);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        content.Headers.ContentEncoding.Add("gzip");
        return content;
    }

    private async Task<UplinkResponse<T>> SendAsync<T>(
        HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken) where T : class
    {
        using var timeout = new CancellationTokenSource(_options.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        using var request = new HttpRequestMessage(method, new Uri(_baseUri, path)) { Content = content };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.BearerToken);
        request.Headers.TryAddWithoutValidation("X-Uplink-Device", _options.DeviceId);
        request.Headers.TryAddWithoutValidation("User-Agent", _options.UserAgent);
        if (!string.IsNullOrWhiteSpace(_options.ExtraHeaderName) && !string.IsNullOrEmpty(_options.ExtraHeaderValue))
        {
            request.Headers.TryAddWithoutValidation(_options.ExtraHeaderName, _options.ExtraHeaderValue);
        }

        try
        {
            using var response = await _http.SendAsync(request, linked.Token).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            var retryAfter = ReadRetryAfter(response.Headers.RetryAfter);

            T? body = null;
            if (response.IsSuccessStatusCode)
            {
                try
                {
                    var text = await response.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);
                    body = JsonSerializer.Deserialize<T>(text, UplinkJson.Options);
                }
                catch (JsonException)
                {
                    body = null;
                }
            }

            return new UplinkResponse<T>(status, body, retryAfter, TransportFailure.None, null);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return new UplinkResponse<T>(null, null, null, TransportFailure.Timeout, "HTTP timeout");
        }
        catch (HttpRequestException ex)
        {
            return new UplinkResponse<T>(null, null, null, TransportFailure.Network, ex.Message);
        }
        catch (IOException ex)
        {
            return new UplinkResponse<T>(null, null, null, TransportFailure.Network, ex.Message);
        }
    }

    private static TimeSpan? ReadRetryAfter(RetryConditionHeaderValue? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value.Delta is { } delta)
        {
            return delta;
        }

        if (value.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }

        return null;
    }
}
