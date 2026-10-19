using System.IO;
using System.Net.Http;
using System.Threading.Channels;
using MyUniverseUplink.Core.Abstractions;
using MyUniverseUplink.Core.Model;
using MyUniverseUplink.Core.Pipeline;
using MyUniverseUplink.Core.Report;
using MyUniverseUplink.Core.Settings;
using MyUniverseUplink.Core.Spool;
using MyUniverseUplink.Core.Transport;
using MyUniverseUplink.Services;
using NINA.WPF.Base.Interfaces.Mediator;

namespace MyUniverseUplink.Runtime;

/// <summary>
/// Composizione del plugin: spool su disco, worker di upload, pipeline miniature e report HTML.
/// HandleImageSaved resta sotto i 50 ms: mappa, decide e accoda; la miniatura si prepara in un thread di lavoro.
/// </summary>
public sealed class UplinkRuntime : IDisposable
{
    private static readonly TimeSpan ThumbStopBudget = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan WorkerStopBudget = TimeSpan.FromSeconds(15);
    private const int MaxRecent = 60;

    private readonly PluginPaths _paths;
    private readonly IUplinkLog _log;
    private readonly HttpClient _http = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(10) })
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    private readonly RetryPolicy _retry = new(SystemJitter.Instance);
    private readonly IClock _clock = SystemClock.Instance;
    private readonly NinaThumbnailRenderer _renderer = new();
    private readonly SemaphoreSlim _applyGate = new(1, 1);
    private readonly Channel<ThumbJob> _thumbs = Channel.CreateUnbounded<ThumbJob>(new UnboundedChannelOptions { SingleReader = true });
    private readonly object _state = new();
    private readonly List<RecentFrame> _recent = new();

    private FileSpoolQueue? _spool;
    private UploadWorker? _worker;
    private CancellationTokenSource? _workerCts;
    private Task? _workerTask;
    private Task? _thumbTask;
    private Timer? _reportTimer;
    private PluginConfig _config = new();
    private ThumbnailPolicy _policy = new(ThumbnailMode.Never);
    private string? _configProblem;
    private ThumbJob? _last;
    private bool _disposed;

    public UplinkRuntime(PluginPaths paths, IUplinkLog log)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public PluginPaths Paths => _paths;

    public PluginConfig Config
    {
        get
        {
            lock (_state)
            {
                return _config;
            }
        }
    }

    /// <summary>Apre lo spool e avvia il consumatore delle miniature. Da chiamare una volta, in Initialize.</summary>
    public void Start()
    {
        _paths.EnsureCreated();
        _spool = new FileSpoolQueue(_paths.Root);
        _thumbTask = Task.Run(ThumbLoopAsync);
        _reportTimer = new Timer(state => { _ = WriteReportAsync(); }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    /// <summary>Applica una configurazione: ferma il worker corrente (con drain limitato), poi ne avvia uno nuovo se la configurazione e' valida.</summary>
    public async Task ApplyAsync(PluginConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        await _applyGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopWorkerAsync(WorkerStopBudget).ConfigureAwait(false);

            lock (_state)
            {
                _config = config;
                _policy = new ThumbnailPolicy(config.ThumbnailMode);
            }

            _log.RegisterSecret(config.BearerToken);
            _log.RegisterSecret(config.ExtraHeaderValue);

            var problem = config.Enabled ? Validate(config) : "disabled";
            lock (_state)
            {
                _configProblem = problem;
            }

            if (problem is not null)
            {
                _log.Info("uplink not sending: " + problem);
                return;
            }

            var worker = CreateWorker(config);
            var cts = new CancellationTokenSource();
            var task = Task.Run(() => worker.RunAsync(cts.Token));
            _ = task.ContinueWith(
                t => _log.Error("upload worker stopped unexpectedly", t.Exception?.GetBaseException()),
                TaskContinuationOptions.OnlyOnFaulted);

            lock (_state)
            {
                _worker = worker;
                _workerCts = cts;
                _workerTask = task;
            }

            _log.Info($"uplink active: batch {config.BatchSize}, thumbnails {config.ThumbnailMode}");
        }
        catch (Exception ex)
        {
            _log.Error("could not apply uplink settings", ex);
        }
        finally
        {
            _applyGate.Release();
        }
    }

    /// <summary>Chiamato da ImageSaved (thread di NINA). Non deve superare 50 ms e non lancia.</summary>
    public void HandleImageSaved(ImageSavedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        PluginConfig config;
        lock (_state)
        {
            config = _config;
        }

        if (!config.Enabled || _spool is null)
        {
            return;
        }

        var context = CreateMappingContext(config);
        var savedAt = _clock.UtcNow;
        if (!NinaFrameMapper.TryMap(args, context, savedAt, null, out var frame, out _) || frame is null)
        {
            return;
        }

        var bitmap = args.Image;
        if (bitmap is null)
        {
            EnqueueFrame(frame);
            RecordRecent(frame);
            return;
        }

        if (bitmap.CanFreeze && !bitmap.IsFrozen)
        {
            bitmap.Freeze();
        }

        var job = new ThumbJob(args, context, savedAt, config.ThumbnailLongEdgePx, config.ThumbnailJpegQuality, RenderThumbnail: false);
        bool wantThumbnail;
        lock (_state)
        {
            wantThumbnail = _policy.ShouldSend(new ThumbnailContext(frame.Target?.Name, frame.Exposure?.Filter, frame.CapturedAtUtc ?? savedAt));
            _last = job;
        }

        if (wantThumbnail)
        {
            job = job with { RenderThumbnail = true };
            if (!_thumbs.Writer.TryWrite(job))
            {
                EnqueueFrame(frame);
                RecordRecent(frame);
            }

            return;
        }

        EnqueueFrame(frame);
        RecordRecent(frame);
    }

    public UplinkStatus GetStatus()
    {
        UploadWorker? worker;
        string? problem;
        bool enabled;
        lock (_state)
        {
            worker = _worker;
            problem = _configProblem;
            enabled = _config.Enabled;
        }

        if (worker is not null)
        {
            return worker.GetStatus();
        }

        var pending = _spool?.PendingCount ?? 0;
        var dead = _spool?.DeadCount ?? 0;
        return new UplinkStatus(
            enabled && problem is null ? UplinkState.Idle : UplinkState.Disabled,
            pending,
            dead,
            0,
            0,
            problem,
            problem is null ? null : _clock.UtcNow,
            null);
    }

    /// <summary>Riporta i frame dead in pending e risveglia il worker. Restituisce quanti ne ha riaccodati.</summary>
    public int RetryDead()
    {
        var spool = _spool;
        if (spool is null)
        {
            return 0;
        }

        var count = spool.RequeueAll();
        WakeWorker();
        return count;
    }

    /// <summary>Risveglia il worker. Un batch parziale parte comunque al piu' tardi dopo 20 s (intervallo del Core).</summary>
    public void WakeWorker()
    {
        UploadWorker? worker;
        lock (_state)
        {
            worker = _worker;
        }

        worker?.Resume();
    }

    /// <summary>Riaccoda l'ultimo frame con miniatura (se la modalita' lo consente). False se non c'e' ancora un'immagine.</summary>
    public bool SendLastImage()
    {
        ThumbJob? last;
        PluginConfig config;
        lock (_state)
        {
            last = _last;
            config = _config;
        }

        if (last is null)
        {
            return false;
        }

        return _thumbs.Writer.TryWrite(last with { RenderThumbnail = config.ThumbnailMode != ThumbnailMode.Never });
    }

    /// <summary>Prova la connessione con GET health usando la configurazione passata (token incluso). Non logga il token.</summary>
    public async Task<string> TestConnectionAsync(PluginConfig config, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(config);
        var problem = Validate(config);
        if (problem is not null)
        {
            return "Settings incomplete: " + problem;
        }

        var client = new UplinkClient(_http, ClientOptions(config));
        var response = await client.GetHealthAsync(cancellationToken).ConfigureAwait(false);

        if (response.Failure == TransportFailure.Timeout)
        {
            return "Unreachable: timeout";
        }

        if (response.Failure == TransportFailure.Network)
        {
            return "Unreachable: " + (response.ErrorMessage ?? "network error");
        }

        return response.StatusCode switch
        {
            401 or 403 => "Auth error (HTTP " + response.StatusCode + "): check the token",
            >= 200 and < 300 when response.Body is { Authenticated: false } => "Auth error: the server rejected the token",
            >= 200 and < 300 when response.Body is { } body =>
                $"OK (schema {body.SchemaVersion}, server {body.ServerVersion ?? "?"}, device {body.DeviceId ?? "?"})",
            >= 200 and < 300 => "Unexpected response from server",
            _ => "Server error: HTTP " + response.StatusCode,
        };
    }

    /// <summary>Scrive il report HTML se abilitato. Non lancia: gli errori vanno nel log.</summary>
    public async Task WriteReportAsync()
    {
        try
        {
            PluginConfig config;
            List<RecentFrame> recent;
            lock (_state)
            {
                config = _config;
                recent = _recent.ToList();
            }

            var spool = _spool;
            if (!config.HtmlReport || spool is null)
            {
                return;
            }

            var dead = spool.ListDead().ToDictionary(d => d.FrameId, StringComparer.Ordinal);
            var rows = recent.Select(r => new ReportFrame(
                r.FrameId,
                r.Target,
                r.Filter,
                r.CapturedAtUtc,
                dead.ContainsKey(r.FrameId)
                    ? "dead"
                    : File.Exists(Path.Combine(spool.PendingDirectory, r.FrameId + ".json")) ? "pending" : "sent",
                dead.TryGetValue(r.FrameId, out var deadLetter) ? deadLetter.Reason : null,
                r.ThumbnailBase64)).ToList();

            var data = new ReportData(_clock.UtcNow, GetStatus(), rows);
            await new HtmlReportWriter().WriteAsync(_paths.ReportFile, data, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Error("HTML report not written", ex);
        }
    }

    /// <summary>Teardown: chiude le miniature in corso (5 s), poi ferma il worker con drain (15 s). Totale massimo 20 s.</summary>
    public async Task StopAsync()
    {
        _thumbs.Writer.TryComplete();
        if (_thumbTask is not null)
        {
            await Task.WhenAny(_thumbTask, Task.Delay(ThumbStopBudget)).ConfigureAwait(false);
        }

        await _applyGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopWorkerAsync(WorkerStopBudget).ConfigureAwait(false);
        }
        finally
        {
            _applyGate.Release();
        }

        _reportTimer?.Dispose();
        _reportTimer = null;
        await WriteReportAsync().ConfigureAwait(false);
        _log.Info($"uplink stopped; pending {_spool?.PendingCount ?? 0}, dead {_spool?.DeadCount ?? 0}");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _reportTimer?.Dispose();
        _http.Dispose();
        _applyGate.Dispose();
    }

    private async Task ThumbLoopAsync()
    {
        try
        {
            await foreach (var job in _thumbs.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                try
                {
                    ProcessThumbJob(job);
                }
                catch (Exception ex)
                {
                    _log.Error("frame processing failed", ex);
                }
            }
        }
        catch (Exception ex)
        {
            _log.Error("thumbnail loop stopped", ex);
        }
    }

    private void ProcessThumbJob(ThumbJob job)
    {
        ThumbnailInfo? thumbnail = null;
        if (job.RenderThumbnail && job.Args.Image is { } bitmap)
        {
            try
            {
                thumbnail = _renderer.Render(bitmap, job.LongEdgePx, job.JpegQuality);
                if (thumbnail is null)
                {
                    _log.Warn("thumbnail above 200 KB even at minimum quality; frame sent without it");
                }
            }
            catch (Exception ex)
            {
                _log.Error("thumbnail rendering failed; frame sent without it", ex);
            }
        }

        if (!NinaFrameMapper.TryMap(job.Args, job.Context, job.SavedAtUtc, thumbnail, out var frame, out var reason) || frame is null)
        {
            _log.Warn("frame dropped after thumbnail step: " + reason);
            return;
        }

        EnqueueFrame(frame);
        RecordRecent(frame);
    }

    private async Task StopWorkerAsync(TimeSpan budget)
    {
        CancellationTokenSource? cts;
        Task? task;
        lock (_state)
        {
            cts = _workerCts;
            task = _workerTask;
            _worker = null;
            _workerCts = null;
            _workerTask = null;
        }

        if (cts is null || task is null)
        {
            return;
        }

        cts.Cancel();
        var finished = await Task.WhenAny(task, Task.Delay(budget)).ConfigureAwait(false);
        if (finished != task)
        {
            _log.Warn("upload worker still draining after the budget; pending frames stay on disk");
        }

        cts.Dispose();
    }

    private UploadWorker CreateWorker(PluginConfig config)
    {
        var options = new UploadWorkerOptions
        {
            Device = new DeviceInfo
            {
                DeviceId = config.DeviceId.Trim(),
                PluginVersion = PluginInfo.PluginVersion,
                NinaVersion = PluginInfo.NinaVersion,
                ProfileId = config.ProfileId,
                ProfileName = config.ProfileName,
            },
            BatchSize = config.BatchSize,
            FlushInterval = TimeSpan.FromSeconds(20),
            DrainTimeout = TimeSpan.FromSeconds(15),
            Enabled = true,
        };

        return new UploadWorker(_spool!, new UplinkClient(_http, ClientOptions(config)), options, _retry, _clock, msg => _log.Info(msg));
    }

    private static UplinkClientOptions ClientOptions(PluginConfig config) => new()
    {
        BaseUrl = new Uri(config.ServerUrl.Trim()),
        DeviceId = config.DeviceId.Trim(),
        BearerToken = config.BearerToken!.Trim(),
        ExtraHeaderName = config.ExtraHeaderName?.Trim(),
        ExtraHeaderValue = config.ExtraHeaderValue,
        Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds),
        UserAgent = "MyUniverseUplink/" + PluginInfo.PluginVersion,
    };

    private static MappingContext CreateMappingContext(PluginConfig config)
        => new(config.ProfileId, FrameAdmission.NormalizeImageTypes(config.ImageTypes), config.RequireTarget);

    /// <summary>Motivo per cui non si puo' inviare, o null se la configurazione e' valida.</summary>
    private static string? Validate(PluginConfig config)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(config.ProfileId))
        {
            problems.Add("no active profile");
        }

        if (string.IsNullOrWhiteSpace(config.BearerToken))
        {
            problems.Add("token not set");
        }

        problems.AddRange(SettingsValidator.Validate(config.ToUplinkSettings()));
        return problems.Count == 0 ? null : string.Join("; ", problems);
    }

    private void EnqueueFrame(FrameRecord frame)
    {
        UploadWorker? worker;
        lock (_state)
        {
            worker = _worker;
        }

        if (worker is not null)
        {
            worker.Enqueue(frame);
        }
        else
        {
            _spool?.Enqueue(frame);
        }
    }

    private void RecordRecent(FrameRecord frame)
    {
        lock (_state)
        {
            _recent.RemoveAll(r => r.FrameId == frame.FrameId);
            _recent.Add(new RecentFrame(
                frame.FrameId,
                frame.Target?.Name,
                frame.Exposure?.Filter,
                frame.CapturedAtUtc,
                frame.Thumbnail?.Base64));
            if (_recent.Count > MaxRecent)
            {
                _recent.RemoveAt(0);
            }
        }
    }

    private sealed record RecentFrame(string FrameId, string? Target, string? Filter, DateTimeOffset? CapturedAtUtc, string? ThumbnailBase64);

    /// <summary>Lavoro per la pipeline miniature. Args e' l'evento NINA, tenuto solo fino all'invio.</summary>
    private sealed record ThumbJob(
        ImageSavedEventArgs Args,
        MappingContext Context,
        DateTimeOffset SavedAtUtc,
        int LongEdgePx,
        int JpegQuality,
        bool RenderThumbnail);
}
