using MyUniverseUplink.Core.Settings;
using Xunit;

namespace MyUniverseUplink.Core.Tests;

public class StreamsSettingsTests
{
    private static StreamSetting Entry(string id = "cam-cielo", string title = "Cielo", string? description = "Camera", string url = "https://example.org/live") =>
        new(id, title, description, url, true);

    private static IReadOnlyList<string> Messages(StreamSetting entry) => StreamsValidator.ValidateEntry(entry);

    [Fact]
    public void ValidEntryHasNoIssues()
    {
        Assert.Empty(StreamsValidator.Validate(new[] { Entry() }));
    }

    [Fact]
    public void TitleOfEightyCharactersIsAccepted()
    {
        Assert.Empty(StreamsValidator.Validate(new[] { Entry(title: new string('t', 80)) }));
    }

    [Fact]
    public void EmptyTitleIsRejected()
    {
        Assert.Contains(Messages(Entry(title: "   ")), m => m.Contains("Title is required"));
    }

    [Fact]
    public void TitleLongerThanEightyIsRejected()
    {
        Assert.Contains(Messages(Entry(title: new string('t', 81))), m => m.Contains("80"));
    }

    [Fact]
    public void DescriptionLongerThanFourHundredIsRejected()
    {
        Assert.Contains(Messages(Entry(description: new string('d', 401))), m => m.Contains("400"));
    }

    [Fact]
    public void PlainHttpIsRejected()
    {
        Assert.Contains(Messages(Entry(url: "http://example.org/live")), m => m.Contains("https://"));
    }

    [Fact]
    public void UrlWithCredentialsIsRejectedWithoutEchoingThem()
    {
        var messages = Messages(Entry(url: "https://user:secret@example.org/live"));

        Assert.Contains(messages, m => m.Contains("credentials"));
        Assert.DoesNotContain(messages, m => m.Contains("secret"));
    }

    [Fact]
    public void UrlLongerThanTwoThousandIsRejected()
    {
        Assert.Contains(Messages(Entry(url: "https://example.org/" + new string('a', 2000))), m => m.Contains("2000"));
    }

    [Fact]
    public void ThirteenEntriesAreRejectedAtListLevel()
    {
        var items = Enumerable.Range(1, 13).Select(i => Entry(id: "s-" + i, url: "https://example.org/" + i)).ToList();

        var issues = StreamsValidator.Validate(items);

        Assert.Contains(issues, i => i.Index == StreamsValidator.ListLevel && i.Message.Contains("12"));
    }

    [Fact]
    public void DuplicateStreamIdFlagsTheSecondEntry()
    {
        var issues = StreamsValidator.Validate(new[] { Entry(id: "same"), Entry(id: "same", url: "https://example.org/b") });

        Assert.Contains(issues, i => i.Index == 1 && i.Message.Contains("used by another"));
    }

    [Theory]
    [InlineData("Cam_Cielo")]
    [InlineData("")]
    [InlineData("cam cielo")]
    public void InvalidStreamIdIsRejected(string id)
    {
        Assert.Contains(Messages(Entry(id: id)), m => m.Contains("Stream ID is invalid"));
    }

    [Fact]
    public void ProblemsAreTiedToTheirEntry()
    {
        var issues = StreamsValidator.Validate(new[] { Entry(id: "ok"), Entry(id: "bad", url: "http://example.org") });

        var issue = Assert.Single(issues);
        Assert.Equal(1, issue.Index);
    }

    [Fact]
    public void SettingsJsonRoundTripsEveryField()
    {
        var items = new[]
        {
            new StreamSetting("cam-cielo", "Cielo", "Camera all-sky", "https://example.org/a", true),
            new StreamSetting("cam-cupola", "Cupola", null, "https://example.org/b", false),
        };

        var json = StreamsSettingsJson.Serialize(items);

        Assert.True(StreamsSettingsJson.TryParse(json, out var parsed));
        Assert.Equal(items, parsed);
        Assert.DoesNotContain("\"description\":null", json);
    }

    [Fact]
    public void EmptyOrMissingJsonMeansNoEntries()
    {
        Assert.True(StreamsSettingsJson.TryParse(null, out var none));
        Assert.Empty(none);
        Assert.True(StreamsSettingsJson.TryParse("[]", out var empty));
        Assert.Empty(empty);
    }

    [Fact]
    public void BrokenJsonIsReportedNotThrown()
    {
        Assert.False(StreamsSettingsJson.TryParse("[{\"title\":", out var items));
        Assert.Empty(items);
    }

    [Fact]
    public void MissingEnabledDefaultsToTrue()
    {
        Assert.True(StreamsSettingsJson.TryParse("[{\"streamId\":\"a\",\"title\":\"t\",\"url\":\"https://x.org\"}]", out var items));
        Assert.True(Assert.Single(items).Enabled);
    }

    [Fact]
    public void SlugFromTitleKeepsAsciiAndAddsFourHex()
    {
        var id = StreamIds.Create("Cielo sopra l'osservatorio", random: new Random(7));

        Assert.Matches("^cielo-sopra-l-osservatorio-[0-9a-f]{4}$", id);
    }

    [Fact]
    public void AccentsAreFoldedToAscii()
    {
        Assert.StartsWith("citta-e-bella-", StreamIds.Create("Città è bella", random: new Random(3)));
    }

    [Fact]
    public void EmptyTitleFallsBackToStream()
    {
        Assert.Matches("^stream-[0-9a-f]{4}$", StreamIds.Create(string.Empty, random: new Random(3)));
    }

    [Fact]
    public void LongTitleStaysWithinSixtyFourCharacters()
    {
        Assert.True(StreamIds.Create(new string('a', 200), random: new Random(3)).Length <= 64);
    }

    [Fact]
    public void GeneratedIdAvoidsTakenValues()
    {
        var first = StreamIds.Create("x", random: new Random(42));
        var second = StreamIds.Create("x", new HashSet<string> { first }, new Random(42));

        Assert.NotEqual(first, second);
    }
}
