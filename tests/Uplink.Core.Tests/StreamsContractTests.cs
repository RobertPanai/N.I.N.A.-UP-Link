using System.Text.Json;
using MyUniverseUplink.Core.Model;
using Xunit;

namespace MyUniverseUplink.Core.Tests;

public class StreamsContractTests
{
    [Fact]
    public void RequestFixtureRoundTripsExactly()
    {
        AssertRoundTrip<StreamsRequest>("streams-v1.request.json");
    }

    [Fact]
    public void ResponseFixtureRoundTripsExactly()
    {
        AssertRoundTrip<StreamsResponse>("streams-v1.response.json");
    }

    [Fact]
    public void RequestFixtureIsReadIntoTypedModel()
    {
        var request = Read<StreamsRequest>("streams-v1.request.json");

        Assert.True(request.StreamsEnabled);
        Assert.Equal("osservatorio-casa", request.Device.DeviceId);
        Assert.Equal(2, request.Streams.Count);
        Assert.Equal("cam-cielo", request.Streams[0].StreamId);
        Assert.Equal(0, request.Streams[0].Order);
        Assert.Equal(string.Empty, request.Streams[1].Description);
    }

    [Fact]
    public void ResponseFixtureIsReadIntoTypedModel()
    {
        var response = Read<StreamsResponse>("streams-v1.response.json");

        Assert.Equal(1, response.Stored);
        Assert.Equal(1, response.Rejected);
        Assert.Equal(0, response.Removed);
        Assert.Equal<bool?>(true, response.Visible);
        Assert.Equal(StreamResultStatus.Stored, response.Results[0].Status);
        Assert.Equal(StreamResultStatus.Rejected, response.Results[1].Status);
        Assert.Equal("host not allowed", response.Results[1].Reason);
    }

    [Fact]
    public void DisabledRequestWritesEmptyListAndFalseFlag()
    {
        var request = new StreamsRequest
        {
            RequestId = "R1",
            Device = new DeviceInfo { DeviceId = "d" },
            StreamsEnabled = false,
        };

        var json = JsonSerializer.Serialize(request, UplinkJson.Options);

        Assert.Contains("\"streamsEnabled\":false", json);
        Assert.Contains("\"streams\":[]", json);
        Assert.DoesNotContain("description", json);
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
