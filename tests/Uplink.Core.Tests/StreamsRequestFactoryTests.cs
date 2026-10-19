using MyUniverseUplink.Core.Model;
using MyUniverseUplink.Core.Settings;
using MyUniverseUplink.Core.Streams;
using Xunit;

namespace MyUniverseUplink.Core.Tests;

public class StreamsRequestFactoryTests
{
    private static readonly DeviceInfo Device = new() { DeviceId = "osservatorio-casa" };

    private static StreamSetting Cielo(bool enabled = true, string? description = "Camera") =>
        new("cam-cielo", "Cielo", description, "https://example.org/cielo", enabled);

    [Fact]
    public void DisabledSettingsSendAnEmptyListAndFalseFlag()
    {
        var request = StreamsRequestFactory.Build(Device, new StreamsSettings(false, new[] { Cielo() }), "R1");

        Assert.False(request.StreamsEnabled);
        Assert.Empty(request.Streams);
        Assert.Equal("R1", request.RequestId);
    }

    [Fact]
    public void OnlyEnabledEntriesAreSentInListOrder()
    {
        var items = new[]
        {
            new StreamSetting("a", "A", null, "https://example.org/a", true),
            new StreamSetting("b", "B", null, "https://example.org/b", false),
            new StreamSetting("c", "C", null, "https://example.org/c", true),
        };

        var request = StreamsRequestFactory.Build(Device, new StreamsSettings(true, items), "R1");

        Assert.Equal(new[] { "a", "c" }, request.Streams.Select(s => s.StreamId));
        Assert.Equal(new[] { 0, 1 }, request.Streams.Select(s => s.Order));
    }

    [Fact]
    public void BlankDescriptionIsOmittedAndValuesAreTrimmed()
    {
        var item = new StreamSetting("cam-cielo", "  Cielo  ", "   ", "  https://example.org/cielo  ", true);

        var entry = Assert.Single(StreamsRequestFactory.Build(Device, new StreamsSettings(true, new[] { item }), "R1").Streams);

        Assert.Equal("Cielo", entry.Title);
        Assert.Equal("https://example.org/cielo", entry.Url);
        Assert.Null(entry.Description);
    }

    [Fact]
    public void FingerprintIsStableForTheSameSet()
    {
        var first = StreamsRequestFactory.Build(Device, new StreamsSettings(true, new[] { Cielo() }), "R1");
        var second = StreamsRequestFactory.Build(Device, new StreamsSettings(true, new[] { Cielo() }), "R2");

        Assert.Equal(
            StreamsRequestFactory.Fingerprint("https://robertpanai.com", first),
            StreamsRequestFactory.Fingerprint("https://robertpanai.com", second));
    }

    [Fact]
    public void FingerprintChangesWithUrlServerOrSwitch()
    {
        var baseline = StreamsRequestFactory.Build(Device, new StreamsSettings(true, new[] { Cielo() }), "R1");
        var otherUrl = StreamsRequestFactory.Build(Device, new StreamsSettings(true, new[] { Cielo() with { Url = "https://example.org/other" } }), "R1");
        var off = StreamsRequestFactory.Build(Device, StreamsSettings.Off, "R1");

        var key = StreamsRequestFactory.Fingerprint("https://robertpanai.com", baseline);

        Assert.NotEqual(key, StreamsRequestFactory.Fingerprint("https://robertpanai.com", otherUrl));
        Assert.NotEqual(key, StreamsRequestFactory.Fingerprint("https://other.example", baseline));
        Assert.NotEqual(key, StreamsRequestFactory.Fingerprint("https://robertpanai.com", off));
    }
}
