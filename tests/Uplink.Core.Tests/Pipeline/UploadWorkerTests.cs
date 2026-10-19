using System.Net;
using MyUniverseUplink.Core.Model;
using MyUniverseUplink.Core.Pipeline;
using MyUniverseUplink.Core.Spool;
using MyUniverseUplink.Core.Tests.Support;
using MyUniverseUplink.Core.Transport;
using Xunit;

namespace MyUniverseUplink.Core.Tests.Pipeline;

public class UploadWorkerTests : IDisposable
{
    private static readonly DateTimeOffset Captured = new(2026, 1, 2, 22, 0, 0, TimeSpan.Zero);

    private readonly TempDir _dir = new();
    private readonly FakeClock _clock = new(Captured.AddHours(1));

    public void Dispose() => _dir.Dispose();

    private static FrameRecord Frame(string id) => new()
    {
        FrameId = id,
        CapturedAtUtc = Captured,
        Target = new TargetInfo { Name = "WitchHead" },
    };

    private static UploadWorkerOptions Options(int batchSize = 25, TimeSpan? flush = null, bool enabled = true) => new()
    {
        Device = new DeviceInfo { DeviceId = "osservatorio-casa" },
        BatchSize = batchSize,
        FlushInterval = flush ?? TimeSpan.FromMilliseconds(50),
        DrainTimeout = TimeSpan.FromSeconds(5),
        Enabled = enabled,
    };

    private static RetryPolicy Retry(int maxAttempts = 8) =>
        new(new FixedJitter(0.0), TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(50), maxAttempts);

    private UploadWorker Worker(FileSpoolQueue spool, IUplinkTransport transport, UploadWorkerOptions options, RetryPolicy? retry = null)
        => new(spool, transport, options, retry ?? Retry(), _clock);

    private static async Task<bool> WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var until = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < until)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(10);
        }

        return condition();
    }

    private static async Task Stop(CancellationTokenSource cts, Task run)
    {
        cts.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task SendsPendingFramesInOneBatchAndClearsTheSpool()
    {
        var spool = new FileSpoolQueue(_dir.Path);
        var transport = new FakeTransport((batch, _) => FakeTransport.Ack(batch));
        var worker = Worker(spool, transport, Options());
        for (var i = 1; i <= 3; i++)
        {
            worker.Enqueue(Frame("s" + i));
        }

        using var cts = new CancellationTokenSource();
        var run = worker.RunAsync(cts.Token);

        Assert.True(await WaitUntil(() => worker.GetStatus().Sent == 3, TimeSpan.FromSeconds(10)));
        await Stop(cts, run);

        var batch = Assert.Single(transport.Batches);
        Assert.Equal(3, batch.Frames.Count);
        Assert.Equal("osservatorio-casa", batch.Device.DeviceId);
        Assert.Equal(26, batch.BatchId.Length);
        var status = worker.GetStatus();
        Assert.Equal(0, status.Pending);
        Assert.Equal(UplinkState.Idle, status.State);
        Assert.NotNull(status.LastSuccessAtUtc);
    }

    [Fact]
    public async Task FullBatchGoesOutAtOnceAndPartialBatchWaitsForTheInterval()
    {
        var spool = new FileSpoolQueue(_dir.Path);
        var transport = new FakeTransport((batch, _) => FakeTransport.Ack(batch));
        var worker = Worker(spool, transport, Options(batchSize: 2, flush: TimeSpan.FromSeconds(1)));

        using var cts = new CancellationTokenSource();
        var run = worker.RunAsync(cts.Token);

        worker.Enqueue(Frame("p1"));
        worker.Enqueue(Frame("p2"));
        Assert.True(await WaitUntil(() => transport.Calls == 1, TimeSpan.FromSeconds(2)));

        worker.Enqueue(Frame("p3"));
        await Task.Delay(300);
        Assert.Equal(1, transport.Calls);

        Assert.True(await WaitUntil(() => transport.Calls == 2, TimeSpan.FromSeconds(5)));
        await Stop(cts, run);
        Assert.Single(transport.Batches[1].Frames);
    }

    [Fact]
    public async Task RestartReplaysPendingFramesOnceWithoutDuplicates()
    {
        var before = new FileSpoolQueue(_dir.Path);
        var offline = Worker(before, new FakeTransport((b, _) => FakeTransport.Ack(b)), Options(flush: TimeSpan.FromHours(1)));
        for (var i = 1; i <= 5; i++)
        {
            offline.Enqueue(Frame("replay" + i));
        }

        var after = new FileSpoolQueue(_dir.Path);
        var transport = new FakeTransport((batch, _) => FakeTransport.Ack(batch));
        var worker = Worker(after, transport, Options());
        using var cts = new CancellationTokenSource();
        var run = worker.RunAsync(cts.Token);

        Assert.True(await WaitUntil(() => worker.GetStatus().Sent == 5, TimeSpan.FromSeconds(10)));
        await Stop(cts, run);

        var sentIds = transport.Batches.SelectMany(b => b.Frames).Select(f => f.FrameId).ToList();
        Assert.Equal(5, sentIds.Count);
        Assert.Equal(5, sentIds.Distinct().Count());
        Assert.Equal(0, after.PendingCount);
    }

    [Fact]
    public async Task RateLimitedHonoursRetryAfterAndResendsTheSameBatchId()
    {
        var spool = new FileSpoolQueue(_dir.Path);
        var stamps = new System.Collections.Concurrent.ConcurrentQueue<DateTimeOffset>();
        var transport = new FakeTransport((batch, index) =>
        {
            stamps.Enqueue(DateTimeOffset.UtcNow);
            return index == 0 ? FakeTransport.Http(429, TimeSpan.FromSeconds(1)) : FakeTransport.Ack(batch);
        });
        var worker = Worker(spool, transport, Options());
        worker.Enqueue(Frame("rl1"));

        using var cts = new CancellationTokenSource();
        var run = worker.RunAsync(cts.Token);

        Assert.True(await WaitUntil(() => worker.GetStatus().Sent == 1, TimeSpan.FromSeconds(10)));
        await Stop(cts, run);

        Assert.Equal(2, transport.Calls);
        Assert.Equal(transport.Batches[0].BatchId, transport.Batches[1].BatchId);
        var times = stamps.ToArray();
        Assert.True(times[1] - times[0] >= TimeSpan.FromSeconds(0.9), $"gap {times[1] - times[0]}");
    }

    [Fact]
    public async Task AuthErrorSuspendsWithoutRetryLoopAndResumeContinues()
    {
        var spool = new FileSpoolQueue(_dir.Path);
        var rejecting = true;
        var transport = new FakeTransport((batch, _) =>
            rejecting ? FakeTransport.Http(401) : FakeTransport.Ack(batch));
        var worker = Worker(spool, transport, Options());
        worker.Enqueue(Frame("au1"));
        worker.Enqueue(Frame("au2"));

        using var cts = new CancellationTokenSource();
        var run = worker.RunAsync(cts.Token);

        Assert.True(await WaitUntil(() => worker.GetStatus().State == UplinkState.AuthError, TimeSpan.FromSeconds(10)));
        await Task.Delay(400);
        Assert.Equal(1, transport.Calls);
        var suspended = worker.GetStatus();
        Assert.Equal(UplinkState.AuthError, suspended.State);
        Assert.Equal(2, suspended.Pending);
        Assert.Contains("token rejected", suspended.LastError);

        rejecting = false;
        worker.Resume();
        Assert.True(await WaitUntil(() => worker.GetStatus().Sent == 2, TimeSpan.FromSeconds(10)));
        await Stop(cts, run);
    }

    [Fact]
    public async Task PayloadTooLargeHalvesTheBatchUntilItFits()
    {
        var spool = new FileSpoolQueue(_dir.Path);
        var transport = new FakeTransport((batch, _) =>
            batch.Frames.Count > 2 ? FakeTransport.Http(413) : FakeTransport.Ack(batch));
        var worker = Worker(spool, transport, Options(batchSize: 4, flush: TimeSpan.FromMilliseconds(20)));
        for (var i = 1; i <= 4; i++)
        {
            worker.Enqueue(Frame("big" + i));
        }

        using var cts = new CancellationTokenSource();
        var run = worker.RunAsync(cts.Token);

        Assert.True(await WaitUntil(() => worker.GetStatus().Sent == 4, TimeSpan.FromSeconds(10)));
        await Stop(cts, run);

        var sizes = transport.Batches.Select(b => b.Frames.Count).ToArray();
        Assert.Equal(new[] { 4, 2, 2 }, sizes);
    }

    [Fact]
    public async Task PermanentErrorMovesTheWholeBatchToDead()
    {
        var spool = new FileSpoolQueue(_dir.Path);
        var transport = new FakeTransport((_, _) => FakeTransport.Http(400));
        var worker = Worker(spool, transport, Options(batchSize: 2));
        worker.Enqueue(Frame("bad1"));
        worker.Enqueue(Frame("bad2"));

        using var cts = new CancellationTokenSource();
        var run = worker.RunAsync(cts.Token);

        Assert.True(await WaitUntil(() => worker.GetStatus().Dead == 2, TimeSpan.FromSeconds(10)));
        await Stop(cts, run);

        Assert.Equal(0, worker.GetStatus().Pending);
        Assert.All(spool.ListDead(), d => Assert.Contains("HTTP 400", d.Reason));
        Assert.Equal(1, transport.Calls);
    }

    [Fact]
    public async Task RejectedFramesAndFramesMissingFromTheResponseGoToDead()
    {
        var spool = new FileSpoolQueue(_dir.Path);
        var transport = new FakeTransport((batch, _) => new UplinkResponse<BatchResponse>(
            200,
            new BatchResponse
            {
                BatchId = batch.BatchId,
                Results =
                [
                    new FrameResult { FrameId = "ok", Status = FrameStatus.Stored },
                    new FrameResult { FrameId = "no", Status = FrameStatus.Rejected, Reason = "bad time" },
                ],
            },
            null,
            TransportFailure.None,
            null));
        var worker = Worker(spool, transport, Options(batchSize: 3));
        worker.Enqueue(Frame("ok"));
        worker.Enqueue(Frame("no"));
        worker.Enqueue(Frame("missing"));

        using var cts = new CancellationTokenSource();
        var run = worker.RunAsync(cts.Token);

        Assert.True(await WaitUntil(() => worker.GetStatus().Dead == 2, TimeSpan.FromSeconds(10)));
        await Stop(cts, run);

        var dead = spool.ListDead().ToDictionary(d => d.FrameId, d => d.Reason);
        Assert.Contains("bad time", dead["no"]);
        Assert.Contains("no result", dead["missing"]);
        Assert.Equal(1, worker.GetStatus().Sent);
    }

    [Fact]
    public async Task TransientFailuresGiveUpAfterMaxAttemptsWithoutLosingFrames()
    {
        var spool = new FileSpoolQueue(_dir.Path);
        var transport = new FakeTransport((_, _) => FakeTransport.Http(500));
        var worker = Worker(spool, transport, Options(), Retry(maxAttempts: 3));
        worker.Enqueue(Frame("flaky"));

        using var cts = new CancellationTokenSource();
        var run = worker.RunAsync(cts.Token);

        Assert.True(await WaitUntil(() => worker.GetStatus().Dead == 1, TimeSpan.FromSeconds(10)));
        await Stop(cts, run);

        Assert.Equal(3, transport.Calls);
        Assert.Contains("gave up after 3 attempts", Assert.Single(spool.ListDead()).Reason);
    }

    [Fact]
    public async Task StopDrainsThePendingQueueBeforeReturning()
    {
        var spool = new FileSpoolQueue(_dir.Path);
        var transport = new FakeTransport((batch, _) => FakeTransport.Ack(batch));
        var worker = Worker(spool, transport, Options(flush: TimeSpan.FromHours(1)));
        for (var i = 1; i <= 3; i++)
        {
            worker.Enqueue(Frame("dr" + i));
        }

        using var cts = new CancellationTokenSource();
        var run = worker.RunAsync(cts.Token);
        await Task.Delay(150);
        Assert.Equal(0, transport.Calls);

        await Stop(cts, run);

        var batch = Assert.Single(transport.Batches);
        Assert.Equal(3, batch.Frames.Count);
        Assert.Equal(0, spool.PendingCount);
    }

    [Fact]
    public async Task StopWithAnUnreachableServerKeepsFramesOnDisk()
    {
        var spool = new FileSpoolQueue(_dir.Path);
        var transport = new FakeTransport((_, _) => FakeTransport.Http(503));
        var worker = Worker(spool, transport, Options(flush: TimeSpan.FromMilliseconds(20)), Retry(maxAttempts: 100));
        worker.Enqueue(Frame("keep1"));
        worker.Enqueue(Frame("keep2"));

        using var cts = new CancellationTokenSource();
        var run = worker.RunAsync(cts.Token);
        Assert.True(await WaitUntil(() => transport.Calls >= 1, TimeSpan.FromSeconds(10)));

        var stopped = DateTimeOffset.UtcNow;
        await Stop(cts, run);

        Assert.True(DateTimeOffset.UtcNow - stopped < TimeSpan.FromSeconds(5));
        Assert.Equal(2, spool.PendingCount);
        Assert.Equal(0, spool.DeadCount);
    }

    [Fact]
    public async Task DisabledWorkerQueuesWithoutSendingUntilEnabled()
    {
        var spool = new FileSpoolQueue(_dir.Path);
        var transport = new FakeTransport((batch, _) => FakeTransport.Ack(batch));
        var worker = Worker(spool, transport, Options(enabled: false));
        worker.Enqueue(Frame("off1"));
        worker.Enqueue(Frame("off2"));

        using var cts = new CancellationTokenSource();
        var run = worker.RunAsync(cts.Token);
        await Task.Delay(200);

        Assert.Equal(0, transport.Calls);
        Assert.Equal(UplinkState.Disabled, worker.GetStatus().State);
        Assert.Equal(2, worker.GetStatus().Pending);

        worker.SetEnabled(true);
        Assert.True(await WaitUntil(() => worker.GetStatus().Sent == 2, TimeSpan.FromSeconds(10)));
        await Stop(cts, run);
    }

    [Fact]
    public async Task EnqueueReturnsWhileTheTransportIsStuck()
    {
        var spool = new FileSpoolQueue(_dir.Path);
        var gate = new TaskCompletionSource<UplinkResponse<BatchResponse>>();
        var stuck = new StuckTransport(gate.Task);
        var worker = new UploadWorker(spool, stuck, Options(batchSize: 1), Retry(), _clock);

        using var cts = new CancellationTokenSource();
        var run = worker.RunAsync(cts.Token);
        worker.Enqueue(Frame("first"));
        Assert.True(await WaitUntil(() => stuck.Calls == 1, TimeSpan.FromSeconds(5)));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        worker.Enqueue(Frame("second"));
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1));
        Assert.Equal(2, worker.GetStatus().Pending);

        gate.SetResult(FakeTransport.Ack(stuck.LastBatch!));
        Assert.True(await WaitUntil(() => worker.GetStatus().Sent == 2, TimeSpan.FromSeconds(10)));
        await Stop(cts, run);
    }

    /// <summary>Prima chiamata: resta appesa finche' il test non la sblocca. Le successive vengono confermate.</summary>
    private sealed class StuckTransport(Task<UplinkResponse<BatchResponse>> firstResponse) : IUplinkTransport
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public FrameBatch? LastBatch { get; private set; }

        public Task<UplinkResponse<BatchResponse>> PostFramesAsync(FrameBatch batch, CancellationToken cancellationToken)
        {
            var index = Interlocked.Increment(ref _calls) - 1;
            LastBatch = batch;
            return index == 0 ? firstResponse : Task.FromResult(FakeTransport.Ack(batch));
        }
    }
}
