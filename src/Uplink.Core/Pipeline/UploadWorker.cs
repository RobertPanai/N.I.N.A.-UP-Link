using System.Threading.Channels;
using MyUniverseUplink.Core.Abstractions;
using MyUniverseUplink.Core.Identity;
using MyUniverseUplink.Core.Model;
using MyUniverseUplink.Core.Spool;
using MyUniverseUplink.Core.Transport;

namespace MyUniverseUplink.Core.Pipeline;

public enum UplinkState
{
    Idle,
    Sending,
    Retrying,
    AuthError,
    Disabled,
}

public sealed record UplinkStatus(
    UplinkState State,
    int Pending,
    int Dead,
    int Sent,
    int SentTonight,
    string? LastError,
    DateTimeOffset? LastErrorAtUtc,
    DateTimeOffset? LastSuccessAtUtc);

public sealed class UploadWorkerOptions
{
    /// <summary>Identita' del dispositivo inserita in ogni batch.</summary>
    public DeviceInfo Device { get; init; } = new();

    public int BatchSize { get; init; } = 25;

    /// <summary>Attesa massima dal primo frame in coda prima di inviare un batch incompleto.</summary>
    public TimeSpan FlushInterval { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>Tempo massimo per svuotare la coda durante lo stop.</summary>
    public TimeSpan DrainTimeout { get; init; } = TimeSpan.FromSeconds(20);

    public bool Enabled { get; init; } = true;
}

/// <summary>
/// Consumatore unico della coda. Enqueue accoda su disco e sveglia il worker (non blocca).
/// Invia batch da BatchSize frame o dopo FlushInterval, applica l'esito per frame, ritenta con
/// RetryPolicy, dimezza il batch su 413, si ferma su 401/403, e allo stop svuota la coda entro DrainTimeout.
/// </summary>
public sealed class UploadWorker
{
    private enum BatchOutcome
    {
        Done,
        Resplit,
        Stop,
    }

    private readonly FileSpoolQueue _spool;
    private readonly IUplinkTransport _transport;
    private readonly UploadWorkerOptions _options;
    private readonly RetryPolicy _retry;
    private readonly IClock _clock;
    private readonly Action<string>? _log;
    private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
    });

    private readonly object _statusLock = new();
    private readonly Dictionary<string, int> _sentByNight = new(StringComparer.Ordinal);
    private UplinkState _state;
    private bool _enabled;
    private int _currentBatchSize;
    private int _sent;
    private string? _lastError;
    private DateTimeOffset? _lastErrorAt;
    private DateTimeOffset? _lastSuccessAt;

    public UploadWorker(
        FileSpoolQueue spool,
        IUplinkTransport transport,
        UploadWorkerOptions options,
        RetryPolicy retry,
        IClock clock,
        Action<string>? log = null)
    {
        _spool = spool ?? throw new ArgumentNullException(nameof(spool));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _retry = retry ?? throw new ArgumentNullException(nameof(retry));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _log = log;

        if (options.BatchSize < 1 || options.FlushInterval <= TimeSpan.Zero || options.DrainTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentException("Batch size, flush interval e drain timeout devono essere positivi.", nameof(options));
        }

        _enabled = options.Enabled;
        _state = _enabled ? UplinkState.Idle : UplinkState.Disabled;
        _currentBatchSize = options.BatchSize;
    }

    /// <summary>Accoda il frame su disco e sveglia il worker. Non attende l'invio.</summary>
    public void Enqueue(FrameRecord frame)
    {
        _spool.Enqueue(frame);
        _wake.Writer.TryWrite(true);
    }

    /// <summary>Attiva o disattiva l'invio. La coda continua ad accettare frame in entrambi i casi.</summary>
    public void SetEnabled(bool enabled)
    {
        lock (_statusLock)
        {
            _enabled = enabled;
            if (!enabled)
            {
                _state = UplinkState.Disabled;
            }
            else if (_state == UplinkState.Disabled)
            {
                _state = UplinkState.Idle;
            }
        }

        _wake.Writer.TryWrite(true);
    }

    /// <summary>Riprende l'invio dopo un errore di autenticazione (il token e' stato corretto).</summary>
    public void Resume()
    {
        lock (_statusLock)
        {
            if (_state == UplinkState.AuthError)
            {
                _state = _enabled ? UplinkState.Idle : UplinkState.Disabled;
            }
        }

        _wake.Writer.TryWrite(true);
    }

    public UplinkStatus GetStatus()
    {
        var pending = _spool.PendingCount;
        var dead = _spool.DeadCount;
        var night = NightId.For(_clock.UtcNow);

        lock (_statusLock)
        {
            var tonight = _sentByNight.TryGetValue(night, out var count) ? count : 0;
            return new UplinkStatus(_state, pending, dead, _sent, tonight, _lastError, _lastErrorAt, _lastSuccessAt);
        }
    }

    /// <summary>Ciclo del worker. Termina quando il token viene annullato, dopo aver svuotato la coda entro DrainTimeout.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!IsSendingAllowed())
                {
                    _ = await WaitSignalAsync(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
                    DrainSignals();
                    continue;
                }

                await WaitForFlushAsync(cancellationToken).ConfigureAwait(false);
                DrainSignals();
                await FlushAsync(cancellationToken, drainMode: false).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Stop richiesto: si passa al drain.
        }

        await DrainOnStopAsync().ConfigureAwait(false);
    }

    private bool IsSendingAllowed()
    {
        lock (_statusLock)
        {
            return _enabled && _state != UplinkState.AuthError;
        }
    }

    /// <summary>Attende finche' c'e' un batch pieno, o il primo frame e' passato da FlushInterval.</summary>
    private async Task WaitForFlushAsync(CancellationToken ct)
    {
        DateTimeOffset? deadline = null;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var pending = _spool.PendingCount;

            if (pending == 0)
            {
                deadline = null;
                _ = await WaitSignalAsync(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
                DrainSignals();
                continue;
            }

            if (pending >= _options.BatchSize)
            {
                return;
            }

            deadline ??= DateTimeOffset.UtcNow + _options.FlushInterval;
            var remaining = deadline.Value - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                return;
            }

            _ = await WaitSignalAsync(remaining, ct).ConfigureAwait(false);
            DrainSignals();
        }
    }

    /// <returns>true se e' arrivato un segnale, false se il timeout e' scaduto.</returns>
    private async Task<bool> WaitSignalAsync(TimeSpan timeout, CancellationToken ct)
    {
        if (timeout == Timeout.InfiniteTimeSpan)
        {
            return await _wake.Reader.WaitToReadAsync(ct).ConfigureAwait(false);
        }

        if (timeout <= TimeSpan.Zero)
        {
            return false;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var read = _wake.Reader.WaitToReadAsync(linked.Token).AsTask();
        var delay = Task.Delay(timeout, linked.Token);
        var finished = await Task.WhenAny(read, delay).ConfigureAwait(false);
        linked.Cancel();
        ct.ThrowIfCancellationRequested();

        return finished == read && read.IsCompletedSuccessfully && read.Result;
    }

    private void DrainSignals()
    {
        while (_wake.Reader.TryRead(out _))
        {
        }
    }

    private async Task FlushAsync(CancellationToken ct, bool drainMode)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (!IsSendingAllowed())
            {
                return;
            }

            var frames = _spool.ReadBatch(CurrentBatchSize());
            if (frames.Count == 0)
            {
                SetState(UplinkState.Idle);
                return;
            }

            var batch = new FrameBatch
            {
                BatchId = UlidFactory.NewUlid(_clock.UtcNow),
                Device = _options.Device,
                Frames = frames.ToList(),
            };

            var outcome = await SendWithRetriesAsync(batch, ct, drainMode).ConfigureAwait(false);
            if (outcome == BatchOutcome.Stop)
            {
                return;
            }
        }
    }

    /// <summary>Invia lo stesso batch (stesso batchId) finche' non ha un esito definitivo.</summary>
    private async Task<BatchOutcome> SendWithRetriesAsync(FrameBatch batch, CancellationToken ct, bool drainMode)
    {
        var attempts = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            SetState(UplinkState.Sending);

            UplinkResponse<BatchResponse> response;
            try
            {
                response = await _transport.PostFramesAsync(batch, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                response = new UplinkResponse<BatchResponse>(null, null, null, TransportFailure.Network, ex.GetType().Name);
            }

            var classification = ErrorClassifier.Classify(response);
            switch (classification.Outcome)
            {
                case SendOutcome.Acknowledged:
                    AcknowledgeBatch(batch, response.Body!);
                    SetBatchSize(_options.BatchSize);
                    SetState(UplinkState.Idle);
                    return BatchOutcome.Done;

                case SendOutcome.PayloadTooLarge:
                    if (batch.Frames.Count > 1)
                    {
                        SetBatchSize(Math.Max(1, batch.Frames.Count / 2));
                        RecordError($"{classification.Reason}; batch size reduced to {CurrentBatchSize()}");
                        return BatchOutcome.Resplit;
                    }

                    DeadLetterAll(batch.Frames, "HTTP 413 for a single frame");
                    RecordError("HTTP 413 for a single frame; frame moved to dead");
                    SetState(UplinkState.Idle);
                    return BatchOutcome.Done;

                case SendOutcome.AuthError:
                    RecordError(classification.Reason);
                    SetState(UplinkState.AuthError);
                    _log?.Invoke("uplink suspended: authentication rejected by server");
                    return BatchOutcome.Stop;

                case SendOutcome.Permanent:
                    RecordError(classification.Reason);
                    DeadLetterAll(batch.Frames, classification.Reason);
                    SetState(UplinkState.Idle);
                    return BatchOutcome.Done;

                default:
                    // Transient e RateLimited: ritento con backoff, fino a MaxAttempts.
                    attempts++;
                    RecordError(classification.Reason);
                    if (attempts >= _retry.MaxAttempts)
                    {
                        DeadLetterAll(batch.Frames, $"gave up after {attempts} attempts: {classification.Reason}");
                        SetState(UplinkState.Idle);
                        return BatchOutcome.Done;
                    }

                    if (drainMode)
                    {
                        // In fase di stop non si aspetta: i frame restano su disco per il prossimo avvio.
                        return BatchOutcome.Stop;
                    }

                    SetState(UplinkState.Retrying);
                    await Task.Delay(_retry.GetDelay(attempts, classification.RetryAfter), ct).ConfigureAwait(false);
                    break;
            }
        }
    }

    private void AcknowledgeBatch(FrameBatch batch, BatchResponse body)
    {
        var results = new Dictionary<string, FrameResult>(StringComparer.Ordinal);
        foreach (var result in body.Results)
        {
            results.TryAdd(result.FrameId, result);
        }

        var completed = new List<string>(batch.Frames.Count);
        var storedByNight = new Dictionary<string, int>(StringComparer.Ordinal);
        var stored = 0;
        var duplicates = 0;
        var dead = 0;

        foreach (var frame in batch.Frames)
        {
            if (!results.TryGetValue(frame.FrameId, out var result))
            {
                _spool.MoveToDead(frame.FrameId, "no result for this frame in the server response");
                dead++;
                continue;
            }

            switch (result.Status)
            {
                case FrameStatus.Stored:
                    completed.Add(frame.FrameId);
                    stored++;
                    var night = NightId.For(frame.CapturedAtUtc ?? _clock.UtcNow);
                    storedByNight[night] = storedByNight.GetValueOrDefault(night) + 1;
                    break;

                case FrameStatus.Duplicate:
                    completed.Add(frame.FrameId);
                    duplicates++;
                    break;

                case FrameStatus.Rejected:
                    _spool.MoveToDead(frame.FrameId, "rejected by server: " + (result.Reason ?? "no reason given"));
                    dead++;
                    break;

                default:
                    _spool.MoveToDead(frame.FrameId, "unknown status from server: " + result.Status);
                    dead++;
                    break;
            }
        }

        _spool.Complete(completed);

        lock (_statusLock)
        {
            _sent += stored;
            foreach (var (night, count) in storedByNight)
            {
                _sentByNight[night] = _sentByNight.GetValueOrDefault(night) + count;
            }

            _lastSuccessAt = _clock.UtcNow;
        }

        _log?.Invoke($"batch {batch.BatchId}: stored {stored}, duplicate {duplicates}, dead {dead}");
    }

    private void DeadLetterAll(IEnumerable<FrameRecord> frames, string reason)
    {
        foreach (var frame in frames)
        {
            _spool.MoveToDead(frame.FrameId, reason);
        }
    }

    private int CurrentBatchSize()
    {
        lock (_statusLock)
        {
            return _currentBatchSize;
        }
    }

    private void SetBatchSize(int size)
    {
        lock (_statusLock)
        {
            _currentBatchSize = size;
        }
    }

    private void SetState(UplinkState state)
    {
        lock (_statusLock)
        {
            if (state == UplinkState.AuthError)
            {
                _state = UplinkState.AuthError;
            }
            else
            {
                _state = _enabled ? state : UplinkState.Disabled;
            }
        }
    }

    private void RecordError(string message)
    {
        lock (_statusLock)
        {
            _lastError = message;
            _lastErrorAt = _clock.UtcNow;
        }

        _log?.Invoke("uplink: " + message);
    }

    private async Task DrainOnStopAsync()
    {
        if (!IsSendingAllowed())
        {
            return;
        }

        using var timeout = new CancellationTokenSource(_options.DrainTimeout);
        try
        {
            await FlushAsync(timeout.Token, drainMode: true).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _log?.Invoke($"drain timeout: {_spool.PendingCount} frame(s) kept on disk");
        }
        catch (Exception ex)
        {
            RecordError("drain failed: " + ex.GetType().Name);
        }
    }
}
