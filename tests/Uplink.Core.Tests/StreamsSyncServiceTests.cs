using System.Net;
using MyUniverseUplink.Core.Abstractions;
using MyUniverseUplink.Core.Activity;
using MyUniverseUplink.Core.Model;
using MyUniverseUplink.Core.Settings;
using MyUniverseUplink.Core.Streams;
using MyUniverseUplink.Core.Tests.Support;
using MyUniverseUplink.Core.Transport;
using Xunit;

namespace MyUniverseUplink.Core.Tests;

public class StreamsSyncServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 20, 0, 0, TimeSpan.Zero);
    private const string Server = "https://robertpanai.com";

    private static readonly StreamSetting Cielo = new("cam-cielo", "Cielo sopra l'osservatorio", "Camera all-sky", "https://astro.example.org/procyon/cielo?tok=SECRET", true);
    private static readonly StreamSetting Cupola = new("cam-cupola", "Dentro la cupola", null, "https://example.org/live/dome", true);

    private static StreamsSettings On(params StreamSetting[] items) => new(true, items);

    private static StreamsSyncRequest Request(IStreamsTransport transport, StreamsSettings settings, StreamsTrigger trigger = StreamsTrigger.Startup) =>
        new(transport, Server, new DeviceInfo { DeviceId = "osservatorio-casa" }, settings, trigger);

    private static StreamsSyncService Service(MemoryStore store, CapturingSink sink, List<TimeSpan>? delays = null, int maxAttempts = 3) =>
        new(
            store,
            sink,
            new RetryPolicy(new FixedJitter(0.5), TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(10), maxAttempts),
            new FakeClock(Now),
            (delay, _) =>
            {
                delays?.Add(delay);
                return Task.CompletedTask;
            });

    [Fact]
    public async Task FirstSyncSendsOnceAndPublishesCountsWithoutUrls()
    {
        var store = new MemoryStore();
        var sink = new CapturingSink();
        var transport = new FakeStreamsTransport((request, _) => Ack(request, "cam-cupola"));
        var service = Service(store, sink);

        await service.SyncAsync(Request(transport, On(Cielo, Cupola)), CancellationToken.None);

        Assert.Single(transport.Requests);
        var status = service.GetStatus();
        Assert.Equal(StreamsState.Published, status.State);
        Assert.Equal(1, status.Stored);
        Assert.Equal(1, status.Rejected);
        Assert.Contains(sink.Messages, m => m == "streams synced: 1 stored, 1 rejected (Dentro la cupola: host not allowed)");
        Assert.DoesNotContain(sink.Messages, m => m.Contains("example.org") || m.Contains("https://") || m.Contains("SECRET"));
    }

    [Fact]
    public async Task UnchangedSettingsAreNotSentAgain()
    {
        var store = new MemoryStore();
        var sink = new CapturingSink();
        var transport = new FakeStreamsTransport((request, _) => Ack(request));
        var service = Service(store, sink);

        await service.SyncAsync(Request(transport, On(Cielo)), CancellationToken.None);
        await service.SyncAsync(Request(transport, On(Cielo), StreamsTrigger.SettingsChanged), CancellationToken.None);

        Assert.Single(transport.Requests);
        Assert.Contains(sink.Messages, m => m == "streams unchanged, not sent");
    }

    [Fact]
    public async Task DisabledWithoutPreviousPublishIsNotSent()
    {
        var transport = new FakeStreamsTransport((request, _) => Ack(request));

        await Service(new MemoryStore(), new CapturingSink()).SyncAsync(Request(transport, StreamsSettings.Off), CancellationToken.None);

        Assert.Empty(transport.Requests);
    }

    [Fact]
    public async Task DisablingAfterPublishSendsOneEmptyListThenStops()
    {
        var store = new MemoryStore();
        var transport = new FakeStreamsTransport((request, _) => Ack(request));
        var service = Service(store, new CapturingSink());

        await service.SyncAsync(Request(transport, On(Cielo)), CancellationToken.None);
        await service.SyncAsync(Request(transport, StreamsSettings.Off, StreamsTrigger.SettingsChanged), CancellationToken.None);
        await service.SyncAsync(Request(transport, StreamsSettings.Off, StreamsTrigger.Startup), CancellationToken.None);

        Assert.Equal(2, transport.Requests.Count);
        Assert.False(transport.Requests[1].StreamsEnabled);
        Assert.Empty(transport.Requests[1].Streams);
        Assert.Equal(StreamsState.NotPublished, service.GetStatus().State);
    }

    [Fact]
    public async Task ManualTriggerSendsEvenWhenUnchanged()
    {
        var transport = new FakeStreamsTransport((request, _) => Ack(request));
        var service = Service(new MemoryStore(), new CapturingSink());

        await service.SyncAsync(Request(transport, On(Cielo)), CancellationToken.None);
        await service.SyncAsync(Request(transport, On(Cielo), StreamsTrigger.Manual), CancellationToken.None);

        Assert.Equal(2, transport.Requests.Count);
    }

    [Fact]
    public async Task TransientErrorIsRetriedThenSucceeds()
    {
        var delays = new List<TimeSpan>();
        var transport = new FakeStreamsTransport((request, index) =>
            index == 0 ? FakeStreamsTransport.Http(503) : Ack(request));
        var service = Service(new MemoryStore(), new CapturingSink(), delays);

        await service.SyncAsync(Request(transport, On(Cielo)), CancellationToken.None);

        Assert.Equal(2, transport.Requests.Count);
        Assert.Single(delays);
        Assert.Equal(StreamsState.Published, service.GetStatus().State);
    }

    [Fact]
    public async Task RateLimitWaitsAtLeastRetryAfter()
    {
        var delays = new List<TimeSpan>();
        var transport = new FakeStreamsTransport((request, index) =>
            index == 0 ? FakeStreamsTransport.Http(429, TimeSpan.FromSeconds(7)) : Ack(request));
        var service = Service(new MemoryStore(), new CapturingSink(), delays);

        await service.SyncAsync(Request(transport, On(Cielo)), CancellationToken.None);

        Assert.True(delays[0] >= TimeSpan.FromSeconds(7));
        Assert.Equal(2, transport.Requests.Count);
    }

    [Fact]
    public async Task AuthErrorSuspendsWithoutRetryAndAuthRestoredResumes()
    {
        var sink = new CapturingSink();
        var transport = new FakeStreamsTransport((request, index) =>
            index == 0 ? FakeStreamsTransport.Http(401) : Ack(request));
        var service = Service(new MemoryStore(), sink);

        await service.SyncAsync(Request(transport, On(Cielo)), CancellationToken.None);

        Assert.Single(transport.Requests);
        var suspended = service.GetStatus();
        Assert.Equal(StreamsState.Suspended, suspended.State);
        Assert.Contains("401", suspended.LastError);
        Assert.Contains(sink.Messages, m => m.StartsWith("streams sync suspended", StringComparison.Ordinal));

        await service.SyncAsync(Request(transport, On(Cielo), StreamsTrigger.AuthRestored), CancellationToken.None);

        Assert.Equal(2, transport.Requests.Count);
        Assert.Equal(StreamsState.Published, service.GetStatus().State);
    }

    [Fact]
    public async Task PermanentErrorIsReportedAndNotRetried()
    {
        var sink = new CapturingSink();
        var transport = new FakeStreamsTransport((_, _) => FakeStreamsTransport.Http(400));
        var service = Service(new MemoryStore(), sink);

        await service.SyncAsync(Request(transport, On(Cielo)), CancellationToken.None);

        Assert.Single(transport.Requests);
        Assert.Equal(StreamsState.Error, service.GetStatus().State);
        Assert.Contains(sink.Messages, m => m.StartsWith("streams sync failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PayloadTooLargeIsNotRetried()
    {
        var transport = new FakeStreamsTransport((_, _) => FakeStreamsTransport.Http(413));

        await Service(new MemoryStore(), new CapturingSink()).SyncAsync(Request(transport, On(Cielo)), CancellationToken.None);

        Assert.Single(transport.Requests);
    }

    [Fact]
    public async Task GivesUpAfterMaxAttempts()
    {
        var transport = new FakeStreamsTransport((_, _) => FakeStreamsTransport.Http(503));
        var service = Service(new MemoryStore(), new CapturingSink(), maxAttempts: 3);

        await service.SyncAsync(Request(transport, On(Cielo)), CancellationToken.None);

        Assert.Equal(3, transport.Requests.Count);
        Assert.Contains("gave up after 3 attempts", service.GetStatus().LastError);
    }

    [Fact]
    public async Task InvalidSettingsAreNeverSent()
    {
        var sink = new CapturingSink();
        var transport = new FakeStreamsTransport((request, _) => Ack(request));
        var broken = new StreamSetting("cam-cielo", "Cielo", null, "http://example.org/live", true);

        await Service(new MemoryStore(), sink).SyncAsync(Request(transport, On(broken)), CancellationToken.None);

        Assert.Empty(transport.Requests);
        Assert.Contains(sink.Messages, m => m.Contains("settings invalid"));
    }

    [Fact]
    public async Task PublishedStateSurvivesARestart()
    {
        var store = new MemoryStore();
        var transport = new FakeStreamsTransport((request, _) => Ack(request));

        await Service(store, new CapturingSink()).SyncAsync(Request(transport, On(Cielo)), CancellationToken.None);
        var restarted = Service(store, new CapturingSink());

        Assert.Equal(StreamsState.Published, restarted.GetStatus().State);
        Assert.Equal(1, restarted.GetStatus().Stored);

        await restarted.SyncAsync(Request(transport, On(Cielo)), CancellationToken.None);
        Assert.Single(transport.Requests);
    }

    [Fact]
    public async Task RemovalIsReportedAsRemoved()
    {
        var sink = new CapturingSink();
        var transport = new FakeStreamsTransport((request, _) => Ack(request));
        var service = Service(new MemoryStore(), sink);

        await service.SyncAsync(Request(transport, On(Cielo)), CancellationToken.None);
        await service.SyncAsync(Request(transport, new StreamsSettings(true, Array.Empty<StreamSetting>()), StreamsTrigger.SettingsChanged), CancellationToken.None);

        Assert.Contains(sink.Messages, m => m.StartsWith("streams removed", StringComparison.Ordinal));
    }

    private static UplinkResponse<StreamsResponse> Ack(StreamsRequest request, params string[] rejectedIds)
    {
        var results = request.Streams.Select(s => rejectedIds.Contains(s.StreamId)
            ? new StreamResult { StreamId = s.StreamId, Status = StreamResultStatus.Rejected, Reason = "host not allowed" }
            : new StreamResult { StreamId = s.StreamId, Status = StreamResultStatus.Stored }).ToList();

        var body = new StreamsResponse
        {
            SchemaVersion = 1,
            RequestId = request.RequestId,
            Results = results,
            Stored = results.Count(r => r.Status == StreamResultStatus.Stored),
            Rejected = results.Count(r => r.Status == StreamResultStatus.Rejected),
            Removed = request.Streams.Count == 0 ? 3 : 0,
            Visible = results.Any(r => r.Status == StreamResultStatus.Stored),
        };
        return new UplinkResponse<StreamsResponse>(200, body, null, TransportFailure.None, null);
    }

    private sealed class FakeStreamsTransport(Func<StreamsRequest, int, UplinkResponse<StreamsResponse>> respond) : IStreamsTransport
    {
        private readonly object _gate = new();
        private readonly List<StreamsRequest> _requests = new();

        public IReadOnlyList<StreamsRequest> Requests
        {
            get
            {
                lock (_gate)
                {
                    return _requests.ToList();
                }
            }
        }

        public Task<UplinkResponse<StreamsResponse>> PutStreamsAsync(StreamsRequest request, CancellationToken cancellationToken)
        {
            int index;
            lock (_gate)
            {
                index = _requests.Count;
                _requests.Add(request);
            }

            return Task.FromResult(respond(request, index));
        }

        public static UplinkResponse<StreamsResponse> Http(int status, TimeSpan? retryAfter = null) =>
            new(status, null, retryAfter, TransportFailure.None, null);
    }

    private sealed class MemoryStore : IStreamsStateStore
    {
        public StreamsSyncState State { get; private set; } = StreamsSyncState.Empty;

        public StreamsSyncState Load() => State;

        public void Save(StreamsSyncState state) => State = state;
    }

    private sealed class CapturingSink : IActivitySink
    {
        private readonly object _gate = new();
        private readonly List<string> _messages = new();

        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (_gate)
                {
                    return _messages.ToList();
                }
            }
        }

        public void Publish(ActivitySeverity severity, string category, string message)
        {
            lock (_gate)
            {
                _messages.Add(message);
            }
        }
    }
}
