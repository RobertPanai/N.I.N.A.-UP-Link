using MyUniverseUplink.Core.Model;
using MyUniverseUplink.Core.Spool;
using MyUniverseUplink.Core.Tests.Support;
using Xunit;

namespace MyUniverseUplink.Core.Tests.Spool;

public class FileSpoolQueueTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private static FrameRecord Frame(string id) => new()
    {
        FrameId = id,
        CapturedAtUtc = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
        Target = new TargetInfo { Name = "WitchHead" },
    };

    [Fact]
    public void EnqueueWritesOneFilePerFrameAndIsIdempotent()
    {
        var spool = new FileSpoolQueue(_dir.Path);

        spool.Enqueue(Frame("a1"));
        spool.Enqueue(Frame("a1"));

        Assert.Equal(1, spool.PendingCount);
        Assert.True(File.Exists(Path.Combine(spool.PendingDirectory, "a1.json")));
    }

    [Fact]
    public void ReadBatchIsFifoByEnqueueTimeAndRespectsTheLimit()
    {
        var spool = new FileSpoolQueue(_dir.Path);
        spool.Enqueue(Frame("bbb"));
        spool.Enqueue(Frame("aaa"));
        spool.Enqueue(Frame("ccc"));
        File.SetLastWriteTimeUtc(Path.Combine(spool.PendingDirectory, "ccc.json"), DateTime.UtcNow.AddHours(-3));
        File.SetLastWriteTimeUtc(Path.Combine(spool.PendingDirectory, "aaa.json"), DateTime.UtcNow.AddHours(-2));
        File.SetLastWriteTimeUtc(Path.Combine(spool.PendingDirectory, "bbb.json"), DateTime.UtcNow.AddHours(-1));

        var batch = spool.ReadBatch(2);

        Assert.Equal(new[] { "ccc", "aaa" }, batch.Select(f => f.FrameId).ToArray());
        Assert.Equal(3, spool.PendingCount);
    }

    [Fact]
    public void ReadBatchRejectsNonPositiveCount()
    {
        var spool = new FileSpoolQueue(_dir.Path);
        Assert.Throws<ArgumentOutOfRangeException>(() => spool.ReadBatch(0));
    }

    [Fact]
    public void CompleteRemovesAcknowledgedFrames()
    {
        var spool = new FileSpoolQueue(_dir.Path);
        spool.Enqueue(Frame("x1"));
        spool.Enqueue(Frame("x2"));

        spool.Complete(new[] { "x1" });

        Assert.Equal(1, spool.PendingCount);
        Assert.Equal("x2", Assert.Single(spool.ReadBatch(10)).FrameId);
    }

    [Fact]
    public void MoveToDeadKeepsTheReasonAndRequeueRestoresTheFrame()
    {
        var spool = new FileSpoolQueue(_dir.Path);
        spool.Enqueue(Frame("d1"));
        spool.Enqueue(Frame("d2"));

        Assert.True(spool.MoveToDead("d1", "rejected by server: bad time"));
        Assert.False(spool.MoveToDead("missing", "nope"));

        Assert.Equal(1, spool.PendingCount);
        Assert.Equal(1, spool.DeadCount);
        var letter = Assert.Single(spool.ListDead());
        Assert.Equal("d1", letter.FrameId);
        Assert.Equal("rejected by server: bad time", letter.Reason);

        Assert.True(spool.Requeue("d1"));
        Assert.Equal(2, spool.PendingCount);
        Assert.Equal(0, spool.DeadCount);
        Assert.False(spool.Requeue("d1"));
    }

    [Fact]
    public void RequeueAllMovesEveryDeadFrameBack()
    {
        var spool = new FileSpoolQueue(_dir.Path);
        spool.Enqueue(Frame("e1"));
        spool.Enqueue(Frame("e2"));
        spool.MoveToDead("e1", "x");
        spool.MoveToDead("e2", "y");

        Assert.Equal(2, spool.RequeueAll());
        Assert.Equal(2, spool.PendingCount);
    }

    [Fact]
    public void OrphanTempFilesAreRemovedOnStartupAndRealFilesKept()
    {
        var first = new FileSpoolQueue(_dir.Path);
        first.Enqueue(Frame("keep"));
        var orphanPending = Path.Combine(first.PendingDirectory, "keep.json." + Guid.NewGuid().ToString("N") + ".tmp");
        var orphanDead = Path.Combine(first.DeadDirectory, "gone.json." + Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllText(orphanPending, "{\"frameId\":\"half");
        File.WriteAllText(orphanDead, "{");

        var second = new FileSpoolQueue(_dir.Path);

        Assert.False(File.Exists(orphanPending));
        Assert.False(File.Exists(orphanDead));
        Assert.Equal(1, second.PendingCount);
        Assert.Equal("keep", Assert.Single(second.ReadBatch(5)).FrameId);
    }

    [Fact]
    public void CorruptFileGoesToDeadAndDoesNotBlockTheRest()
    {
        var spool = new FileSpoolQueue(_dir.Path);
        File.WriteAllText(Path.Combine(spool.PendingDirectory, "bad.json"), "{ this is not json");
        File.SetLastWriteTimeUtc(Path.Combine(spool.PendingDirectory, "bad.json"), DateTime.UtcNow.AddHours(-5));
        spool.Enqueue(Frame("good"));

        var batch = spool.ReadBatch(10);

        Assert.Equal("good", Assert.Single(batch).FrameId);
        Assert.Equal(1, spool.PendingCount);
        Assert.Equal(1, spool.DeadCount);
        var letter = Assert.Single(spool.ListDead());
        Assert.Equal("bad", letter.FrameId);
        Assert.Contains("corrupt", letter.Reason);
    }

    [Fact]
    public void FrameIdThatDoesNotMatchTheFileNameGoesToDead()
    {
        var spool = new FileSpoolQueue(_dir.Path);
        spool.Enqueue(Frame("right"));
        File.Copy(
            Path.Combine(spool.PendingDirectory, "right.json"),
            Path.Combine(spool.PendingDirectory, "wrong.json"));

        var batch = spool.ReadBatch(10);

        Assert.Equal("right", Assert.Single(batch).FrameId);
        Assert.Equal("wrong", Assert.Single(spool.ListDead()).FrameId);
    }

    [Fact]
    public void PendingFramesSurviveARestartAndReplayIdentically()
    {
        var before = new FileSpoolQueue(_dir.Path);
        foreach (var id in new[] { "r1", "r2", "r3" })
        {
            before.Enqueue(Frame(id));
        }

        var after = new FileSpoolQueue(_dir.Path);

        Assert.Equal(3, after.PendingCount);
        Assert.Equal(new[] { "r1", "r2", "r3" }, after.ReadBatch(10).Select(f => f.FrameId).OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task ConcurrentProducersAndConsumerLoseNothing()
    {
        var spool = new FileSpoolQueue(_dir.Path);
        const int producers = 8;
        const int perProducer = 50;

        var writers = Enumerable.Range(0, producers).Select(p => Task.Run(() =>
        {
            for (var i = 0; i < perProducer; i++)
            {
                spool.Enqueue(Frame($"p{p:D2}-{i:D3}"));
            }
        })).ToArray();

        var completed = 0;
        var reader = Task.Run(() =>
        {
            for (var i = 0; i < 20; i++)
            {
                var ids = spool.ReadBatch(25).Take(5).Select(f => f.FrameId).ToList();
                spool.Complete(ids);
                Interlocked.Add(ref completed, ids.Count);
            }
        });

        await Task.WhenAll(writers.Append(reader));

        Assert.Equal(producers * perProducer - completed, spool.PendingCount);
        Assert.Equal(0, spool.DeadCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("../evil")]
    [InlineData("a/b")]
    [InlineData("has space")]
    public void UnsafeFrameIdsAreRejectedBeforeTouchingTheDisk(string id)
    {
        var spool = new FileSpoolQueue(_dir.Path);
        Assert.Throws<ArgumentException>(() => spool.Enqueue(Frame(id)));
        Assert.Equal(0, spool.PendingCount);
    }
}
