using MyUniverseUplink.Core.Settings;
using Xunit;

namespace MyUniverseUplink.Core.Tests;

public class BootstrapStreamsTests
{
    [Fact]
    public void StreamsFieldsAreReadAndNamedAsApplied()
    {
        var result = BootstrapSettings.Parse("""
            {
              "streamsEnabled": true,
              "streams": [
                { "title": "Cielo sopra l'osservatorio", "description": "Camera all-sky", "url": "https://astro.example.org/cielo" },
                { "title": "Dentro la cupola", "url": "https://example.org/live/dome", "enabled": false }
              ]
            }
            """);

        Assert.NotNull(result);
        Assert.Equal<bool?>(true, result!.Settings.StreamsEnabled);
        var streams = result.Settings.Streams!;
        Assert.Equal(2, streams.Count);
        Assert.True(streams[0].Enabled);
        Assert.False(streams[1].Enabled);
        Assert.Null(streams[1].Description);
        Assert.Contains("streams", result.Applied);
        Assert.Contains("streamsEnabled", result.Applied);
        Assert.Empty(result.Rejected);
    }

    [Fact]
    public void EmptyStreamListIsValidAndClears()
    {
        var result = BootstrapSettings.Parse("""{ "streams": [] }""");

        Assert.NotNull(result);
        Assert.NotNull(result!.Settings.Streams);
        Assert.Empty(result.Settings.Streams!);
        Assert.Contains("streams", result.Applied);
    }

    [Theory]
    [InlineData("""{ "streams": [ { "title": "", "url": "https://x.org" } ] }""", "streams")]
    [InlineData("""{ "streams": [ { "title": "T", "url": "http://x.org" } ] }""", "streams")]
    [InlineData("""{ "streams": [ { "title": "T", "url": "https://u:p@x.org" } ] }""", "streams")]
    [InlineData("""{ "streams": [ { "title": "T" } ] }""", "streams")]
    [InlineData("""{ "streams": [ { "title": "T", "url": "https://x.org", "enabled": "yes" } ] }""", "streams")]
    [InlineData("""{ "streams": "nope" }""", "streams")]
    [InlineData("""{ "streams": [ "https://x.org" ] }""", "streams")]
    [InlineData("""{ "streamsEnabled": 1 }""", "streamsEnabled")]
    public void InvalidStreamFieldsAreDiscardedAndNamed(string json, string field)
    {
        var result = BootstrapSettings.Parse(json);

        Assert.NotNull(result);
        Assert.Contains(field, result!.Rejected);
        Assert.DoesNotContain(field, result.Applied);
        Assert.Null(result.Settings.Streams);
    }

    [Fact]
    public void MoreThanTwelveStreamsAreRejected()
    {
        var entries = string.Join(",", Enumerable.Range(1, 13).Select(i => "{\"title\":\"T" + i + "\",\"url\":\"https://x.org/" + i + "\"}"));

        var result = BootstrapSettings.Parse("{\"streams\":[" + entries + "]}");

        Assert.NotNull(result);
        Assert.Contains("streams", result!.Rejected);
    }

    [Fact]
    public void LegacyDocumentWithoutStreamsStillParsesTheSameWay()
    {
        var result = BootstrapSettings.Parse("""{ "serverUrl": "https://robertpanai.com", "batchSize": 10 }""");

        Assert.NotNull(result);
        Assert.Null(result!.Settings.Streams);
        Assert.Null(result.Settings.StreamsEnabled);
        Assert.Equal(2, result.Applied.Count);
    }

    [Fact]
    public void MergeKeepsIdsOfEntriesWithTheSameTitle()
    {
        var current = new List<StreamSetting> { new("cam-cielo", "Cielo", null, "https://a.org/1", true) };
        var incoming = new List<BootstrapStream>
        {
            new("Cielo", "nuova descrizione", "https://a.org/2", false),
            new("Nuova", null, "https://b.org/3", true),
        };

        var merged = StreamsBootstrap.Merge(current, incoming);

        Assert.Equal("cam-cielo", merged[0].StreamId);
        Assert.False(merged[0].Enabled);
        Assert.Equal("nuova descrizione", merged[0].Description);
        Assert.Matches("^nuova-[0-9a-f]{4}$", merged[1].StreamId);
        Assert.NotEqual(merged[0].StreamId, merged[1].StreamId);
    }
}
