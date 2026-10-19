using System.Text.Json;
using MyUniverseUplink.Core.Model;
using Xunit;

namespace MyUniverseUplink.Core.Tests;

public class ContractTests
{
    [Fact]
    public void RequestFixtureRoundTripsExactly()
    {
        AssertRoundTrip<FrameBatch>("batch-v1.request.json");
    }

    [Fact]
    public void ResponseFixtureRoundTripsExactly()
    {
        AssertRoundTrip<BatchResponse>("batch-v1.response.json");
    }

    [Fact]
    public void HealthFixtureRoundTripsExactly()
    {
        AssertRoundTrip<HealthResponse>("health-v1.json");
    }

    [Fact]
    public void RequestFixtureIsReadIntoTypedModel()
    {
        var batch = Read<FrameBatch>("batch-v1.request.json");

        Assert.Equal(1, batch.SchemaVersion);
        Assert.Equal("01JBQ0000000000000000000A1", batch.BatchId);
        Assert.Equal("osservatorio-casa", batch.Device.DeviceId);
        Assert.Equal(3, batch.Frames.Count);

        var first = batch.Frames[0];
        Assert.Equal("9f2c1a0e5b7d4c3a9f2c1a0e5b7d4c3a", first.FrameId);
        Assert.Equal(new DateTimeOffset(2025, 11, 7, 9, 16, 58, 892, TimeSpan.Zero), first.CapturedAtUtc);
        Assert.Equal(900.0, first.Exposure?.DurationSeconds);
        Assert.Equal(56, first.Exposure?.Gain);
        Assert.Equal(812, first.Quality?.StarCount);
        Assert.Equal("WitchHead_Ha_900s_0012.fits", first.Source?.FileName);

        var rejected = batch.Frames[2];
        Assert.Null(rejected.CapturedAtUtc);
        Assert.Equal("WitchHead", rejected.Target?.Name);
    }

    [Fact]
    public void TolerantReaderIgnoresUnknownFieldsAndMissingOptionals()
    {
        const string json = """
            {"schemaVersion":1,"batchId":"B1","futureField":{"x":[1,2,3]},
             "device":{"deviceId":"d","futureDevice":true},
             "frames":[{"frameId":"abc","imageType":"FLAT","capturedAtUtc":"2026-01-02T03:04:05.006Z","newThing":42}]}
            """;

        var batch = JsonSerializer.Deserialize<FrameBatch>(json, UplinkJson.Options)!;

        Assert.Equal("d", batch.Device.DeviceId);
        Assert.Single(batch.Frames);
        Assert.Equal("FLAT", batch.Frames[0].ImageType);
        Assert.Null(batch.Frames[0].Target);
    }

    [Fact]
    public void DatesAreWrittenAsUtcWithMilliseconds()
    {
        var frame = new FrameRecord
        {
            FrameId = "f1",
            CapturedAtUtc = new DateTimeOffset(2026, 3, 4, 5, 6, 7, 8, TimeSpan.FromHours(2)),
        };

        var json = JsonSerializer.Serialize(frame, UplinkJson.Options);

        Assert.Contains("\"capturedAtUtc\":\"2026-03-04T03:06:07.008Z\"", json);
    }

    [Fact]
    public void PayloadNeverContainsObservatoryCoordinatesOrAbsolutePaths()
    {
        var json = File.ReadAllText(RepoPaths.Contract("batch-v1.request.json"));
        var written = JsonSerializer.Serialize(Read<FrameBatch>("batch-v1.request.json"), UplinkJson.Options);

        foreach (var forbidden in new[] { "latitude", "longitude", "siteLat", "siteLon", ":\\", "C:/", "/home/" })
        {
            Assert.DoesNotContain(forbidden, written, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain("latitude", json, StringComparison.OrdinalIgnoreCase);
    }

    private static T Read<T>(string fileName)
    {
        var text = File.ReadAllText(RepoPaths.Contract(fileName));
        return JsonSerializer.Deserialize<T>(text, UplinkJson.Options)
            ?? throw new InvalidOperationException($"Fixture non leggibile: {fileName}");
    }

    private static void AssertRoundTrip<T>(string fileName)
    {
        var original = JsonDocument.Parse(File.ReadAllText(RepoPaths.Contract(fileName))).RootElement;
        var model = Read<T>(fileName);
        var written = JsonDocument.Parse(JsonSerializer.Serialize(model, UplinkJson.Options)).RootElement;

        var equal = JsonEquivalence.AreEquivalent(original, written, out var difference);
        Assert.True(equal, $"{fileName} non coincide dopo il roundtrip: {difference}");
    }
}
