using System.Threading.Tasks;
using MyUniverseUplink.Core.Activity;
using MyUniverseUplink.Core.Tests.Support;
using Xunit;

namespace MyUniverseUplink.Core.Tests;

public class ActivityFeedTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 19, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Entries_keep_publish_order_and_utc_timestamps()
    {
        var clock = new FakeClock(Start);
        var feed = new ActivityFeed(clock);

        feed.Publish(ActivitySeverity.Info, "plugin", "started");
        clock.UtcNow = Start.AddSeconds(5);
        feed.Publish(ActivitySeverity.Ok, "batch", "sent 3 frame(s)");
        clock.UtcNow = Start.AddSeconds(9);
        feed.Publish(ActivitySeverity.Warn, "retry", "retry in 4 s");

        var snapshot = feed.Snapshot();
        Assert.Equal(3, snapshot.Count);
        Assert.Equal(new[] { "started", "sent 3 frame(s)", "retry in 4 s" }, snapshot.Select(e => e.Message));
        Assert.Equal(new[] { ActivitySeverity.Info, ActivitySeverity.Ok, ActivitySeverity.Warn }, snapshot.Select(e => e.Severity));
        Assert.Equal(new[] { Start, Start.AddSeconds(5), Start.AddSeconds(9) }, snapshot.Select(e => e.UtcTimestamp));
        Assert.Equal(new long[] { 1, 2, 3 }, snapshot.Select(e => e.Id));
        Assert.All(snapshot, e => Assert.Equal(TimeSpan.Zero, e.UtcTimestamp.Offset));
    }

    [Fact]
    public void Buffer_keeps_only_the_last_capacity_entries()
    {
        var feed = new ActivityFeed(new FakeClock(Start));
        const int total = ActivityFeed.Capacity + 100;

        for (var i = 1; i <= total; i++)
        {
            feed.Publish(ActivitySeverity.Info, "test", "event " + i);
        }

        var snapshot = feed.Snapshot();
        Assert.Equal(ActivityFeed.Capacity, snapshot.Count);
        Assert.Equal("event 101", snapshot[0].Message);
        Assert.Equal("event " + total, snapshot[^1].Message);
        Assert.Equal(Enumerable.Range(101, ActivityFeed.Capacity).Select(i => (long)i), snapshot.Select(e => e.Id));
    }

    [Fact]
    public void Clear_empties_the_feed_and_ids_keep_growing()
    {
        var feed = new ActivityFeed(new FakeClock(Start));
        feed.Publish(ActivitySeverity.Info, "a", "one");
        feed.Publish(ActivitySeverity.Info, "a", "two");

        feed.Clear();
        Assert.Empty(feed.Snapshot());

        feed.Publish(ActivitySeverity.Error, "b", "three");
        var entry = Assert.Single(feed.Snapshot());
        Assert.Equal(3, entry.Id);
        Assert.Equal("three", entry.Message);
    }

    [Fact]
    public void EntryAdded_is_raised_and_a_faulty_subscriber_does_not_break_publishing()
    {
        var feed = new ActivityFeed(new FakeClock(Start));
        var received = new List<string>();
        feed.EntryAdded += (_, _) => throw new InvalidOperationException("subscriber bug");
        feed.EntryAdded += (_, e) => received.Add(e.Message);

        feed.Publish(ActivitySeverity.Info, "x", "hello");

        Assert.Equal(new[] { "hello" }, received);
        Assert.Single(feed.Snapshot());
    }

    [Fact]
    public async Task Concurrent_publishing_keeps_ids_unique_and_ordered()
    {
        var feed = new ActivityFeed(new FakeClock(Start));
        const int threads = 8;
        const int perThread = 1000;

        var tasks = Enumerable.Range(0, threads).Select(t => Task.Run(() =>
        {
            for (var i = 0; i < perThread; i++)
            {
                feed.Publish(ActivitySeverity.Info, "load", $"thread {t} item {i}");
                if (i % 100 == 0)
                {
                    _ = feed.Snapshot();
                }
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        var snapshot = feed.Snapshot();
        Assert.Equal(ActivityFeed.Capacity, snapshot.Count);
        var ids = snapshot.Select(e => e.Id).ToArray();
        Assert.Equal(ids.Distinct().Count(), ids.Length);
        Assert.True(ids.Zip(ids.Skip(1), (a, b) => b == a + 1).All(x => x));
        Assert.Equal((long)(threads * perThread), ids[^1]);
    }

    [Fact]
    public void Messages_never_keep_tokens_urls_query_strings_or_absolute_paths()
    {
        var feed = new ActivityFeed(new FakeClock(Start));

        feed.Publish(ActivitySeverity.Error, "error", "Authorization: Bearer abc123SECRETtoken failed");
        feed.Publish(ActivitySeverity.Error, "error", "GET https://example.invalid/api/uplink/health?token=zz9SECRET99 failed");
        feed.Publish(ActivitySeverity.Warn, "error", @"could not read C:\Users\demo\AppData\Local\NINA\secret.json");
        feed.Publish(ActivitySeverity.Info, "error", "line one\nline two\tend");

        var messages = feed.Snapshot().Select(e => e.Message).ToArray();
        var joined = string.Join("\n", messages);
        Assert.DoesNotContain("abc123SECRETtoken", joined);
        Assert.DoesNotContain("zz9SECRET99", joined);
        Assert.DoesNotContain("https://", joined);
        Assert.DoesNotContain("?token", joined);
        Assert.DoesNotContain("C:\\", joined);
        Assert.DoesNotContain("secret.json", joined);
        Assert.Equal("line one line two end", messages[3]);
    }

    [Fact]
    public void Long_messages_are_truncated()
    {
        var feed = new ActivityFeed(new FakeClock(Start));
        feed.Publish(ActivitySeverity.Info, "x", new string('a', 1000));

        var message = Assert.Single(feed.Snapshot()).Message;
        Assert.True(message.Length <= ActivityText.MaxLength + 3);
        Assert.EndsWith("...", message);
    }

    [Fact]
    public void Severity_labels_are_fixed_width_and_ordered()
    {
        Assert.True(ActivitySeverity.Info < ActivitySeverity.Ok);
        Assert.True(ActivitySeverity.Ok < ActivitySeverity.Warn);
        Assert.True(ActivitySeverity.Warn < ActivitySeverity.Error);
        Assert.All(Enum.GetValues<ActivitySeverity>(), s => Assert.Equal(5, s.Label().Length));
    }
}
