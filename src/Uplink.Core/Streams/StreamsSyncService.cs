using MyUniverseUplink.Core.Abstractions;
using MyUniverseUplink.Core.Activity;
using MyUniverseUplink.Core.Identity;
using MyUniverseUplink.Core.Model;
using MyUniverseUplink.Core.Settings;
using MyUniverseUplink.Core.Transport;

namespace MyUniverseUplink.Core.Streams;

/// <summary>Motivo della sincronizzazione. Manual forza l'invio; AuthRestored lo forza dopo un 401/403.</summary>
public enum StreamsTrigger
{
    Startup,
    SettingsChanged,
    ProfileChanged,
    AuthRestored,
    Manual,
}

public enum StreamsState
{
    NotPublished,
    Published,
    Error,
    Suspended,
}

/// <summary>Stato per la pagina Opzioni. LastError non contiene mai URL.</summary>
public sealed record StreamsStatus(
    StreamsState State,
    int Stored,
    int Rejected,
    bool? Visible,
    DateTimeOffset? LastSuccessUtc,
    string? LastError,
    DateTimeOffset? LastErrorUtc)
{
    public static StreamsStatus Initial { get; } = new(StreamsState.NotPublished, 0, 0, null, null, null, null);
}

/// <summary>Una richiesta di sincronizzazione: client, server (chiave per l'impronta), dispositivo, impostazioni e motivo.</summary>
public sealed record StreamsSyncRequest(
    IStreamsTransport Transport,
    string ServerKey,
    DeviceInfo Device,
    StreamsSettings Settings,
    StreamsTrigger Trigger);

/// <summary>
/// Sincronizza l'insieme delle dirette col sito. Invia solo se l'insieme e' cambiato rispetto all'ultimo invio riuscito
/// (o se e' richiesto con Manual/AuthRestored), ritenta gli errori transitori con RetryPolicy, sospende su 401/403 e
/// segnala gli errori permanenti senza riprovare. Ogni esito va sul feed Activity, senza URL.
/// </summary>
public sealed class StreamsSyncService
{
    private const string Category = "streams";

    private readonly IStreamsStateStore _store;
    private readonly IActivitySink? _activity;
    private readonly RetryPolicy _retry;
    private readonly IClock _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _lock = new();
    private StreamsStatus _status;
    private bool _pending;

    public StreamsSyncService(
        IStreamsStateStore store,
        IActivitySink? activity,
        RetryPolicy retry,
        IClock? clock = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _activity = activity;
        _retry = retry ?? throw new ArgumentNullException(nameof(retry));
        _clock = clock ?? SystemClock.Instance;
        _delay = delay ?? ((d, ct) => Task.Delay(d, ct));
        _status = StatusFrom(_store.Load());
    }

    public StreamsStatus GetStatus()
    {
        lock (_lock)
        {
            return _status;
        }
    }

    /// <summary>Esegue una sincronizzazione. Non lancia: ogni esito, anche inatteso, finisce sul feed e nello stato.</summary>
    public async Task SyncAsync(StreamsSyncRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Publish(ActivitySeverity.Info, "streams sync interrupted");
            return;
        }

        try
        {
            await RunAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Publish(ActivitySeverity.Info, "streams sync interrupted");
        }
        catch (Exception ex)
        {
            Fail("unexpected " + ex.GetType().Name, ActivitySeverity.Error);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RunAsync(StreamsSyncRequest request, CancellationToken ct)
    {
        ResumeFromSuspension();

        var problems = StreamsValidator.Validate(request.Settings.Items);
        if (problems.Count > 0)
        {
            var text = string.Join("; ", problems.Select(p => p.Index >= 0 ? "entry " + (p.Index + 1) + ": " + p.Message : p.Message));
            Fail("settings invalid, not sent (" + text + ")", ActivitySeverity.Warn);
            return;
        }

        var body = StreamsRequestFactory.Build(request.Device, request.Settings, UlidFactory.NewUlid(_clock.UtcNow));
        var fingerprint = StreamsRequestFactory.Fingerprint(request.ServerKey, body);
        var saved = _store.Load();
        var forced = request.Trigger is StreamsTrigger.Manual or StreamsTrigger.AuthRestored;

        if (!forced && !body.StreamsEnabled && saved.Fingerprint is null)
        {
            Publish(ActivitySeverity.Info, "streams off and nothing published before, not sent");
            return;
        }

        if (!forced && !IsPending() && string.Equals(fingerprint, saved.Fingerprint, StringComparison.Ordinal))
        {
            Publish(ActivitySeverity.Info, "streams unchanged, not sent");
            return;
        }

        var reply = await SendWithRetryAsync(request.Transport, body, ct).ConfigureAwait(false);
        if (reply is not null)
        {
            Succeed(body, fingerprint, reply);
        }
    }

    private async Task<StreamsResponse?> SendWithRetryAsync(IStreamsTransport transport, StreamsRequest body, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var response = await transport.PutStreamsAsync(body, ct).ConfigureAwait(false);
            var verdict = ErrorClassifier.Classify(response);

            switch (verdict.Outcome)
            {
                case SendOutcome.Acknowledged when response.Body is not null:
                    return response.Body;

                case SendOutcome.AuthError:
                    Suspend(verdict.Reason);
                    return null;

                case SendOutcome.PayloadTooLarge:
                case SendOutcome.Permanent:
                    Fail(verdict.Reason, ActivitySeverity.Error);
                    return null;

                default:
                    if (attempt >= _retry.MaxAttempts)
                    {
                        Fail("gave up after " + attempt + " attempts: " + verdict.Reason, ActivitySeverity.Error);
                        return null;
                    }

                    var delay = _retry.GetDelay(attempt, verdict.RetryAfter);
                    Publish(
                        ActivitySeverity.Warn,
                        "streams sync retry " + attempt + " in " + (long)Math.Ceiling(delay.TotalSeconds) + " s (" + verdict.Reason + ")");
                    await _delay(delay, ct).ConfigureAwait(false);
                    break;
            }
        }
    }

    private void Succeed(StreamsRequest body, string fingerprint, StreamsResponse reply)
    {
        var now = _clock.UtcNow;
        var titles = body.Streams.ToDictionary(s => s.StreamId, s => s.Title, StringComparer.Ordinal);
        var details = reply.Results
            .Where(r => r.Status != StreamResultStatus.Stored)
            .Select(r => (titles.TryGetValue(r.StreamId, out var title) ? title : r.StreamId)
                + ": " + (string.IsNullOrWhiteSpace(r.Reason) ? "rejected" : r.Reason))
            .ToList();

        var severity = ActivitySeverity.Ok;
        string message;
        if (body.Streams.Count == 0)
        {
            message = "streams removed (" + reply.Removed + " from the site)";
        }
        else
        {
            message = "streams synced: " + reply.Stored + " stored, " + reply.Rejected + " rejected"
                + (details.Count > 0 ? " (" + string.Join("; ", details) + ")" : string.Empty);
            if (reply.Visible == false)
            {
                message += ", section hidden on the site";
            }

            if (reply.Rejected > 0)
            {
                severity = ActivitySeverity.Warn;
            }
        }

        Publish(severity, message);
        _store.Save(new StreamsSyncState(fingerprint, reply.Stored, reply.Rejected, reply.Visible, now));

        lock (_lock)
        {
            _pending = false;
            _status = new StreamsStatus(
                reply.Stored > 0 && body.Streams.Count > 0 ? StreamsState.Published : StreamsState.NotPublished,
                reply.Stored,
                reply.Rejected,
                reply.Visible,
                now,
                null,
                null);
        }
    }

    private void Suspend(string reason)
    {
        var now = _clock.UtcNow;
        var text = reason + "; save the token again to resume";
        lock (_lock)
        {
            _pending = true;
            _status = _status with { State = StreamsState.Suspended, LastError = text, LastErrorUtc = now };
        }

        Publish(ActivitySeverity.Error, "streams sync suspended: " + text);
    }

    private void Fail(string reason, ActivitySeverity severity)
    {
        var now = _clock.UtcNow;
        lock (_lock)
        {
            _pending = true;
            _status = _status with { State = StreamsState.Error, LastError = reason, LastErrorUtc = now };
        }

        Publish(severity, "streams sync failed: " + reason);
    }

    /// <summary>Ogni nuovo tentativo riprende da una sospensione: il token o le impostazioni potrebbero essere cambiati.</summary>
    private void ResumeFromSuspension()
    {
        lock (_lock)
        {
            if (_status.State == StreamsState.Suspended)
            {
                _status = _status with
                {
                    State = _status.Stored > 0 ? StreamsState.Published : StreamsState.NotPublished,
                    LastError = null,
                    LastErrorUtc = null,
                };
            }
        }
    }

    private bool IsPending()
    {
        lock (_lock)
        {
            return _pending;
        }
    }

    private static StreamsStatus StatusFrom(StreamsSyncState saved) => new(
        saved.Fingerprint is not null && saved.Stored > 0 ? StreamsState.Published : StreamsState.NotPublished,
        saved.Stored,
        saved.Rejected,
        saved.Visible,
        saved.SyncedAtUtc,
        null,
        null);

    private void Publish(ActivitySeverity severity, string message) => _activity?.Publish(severity, Category, message);
}
