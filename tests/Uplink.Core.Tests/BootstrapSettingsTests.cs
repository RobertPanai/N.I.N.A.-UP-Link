using MyUniverseUplink.Core.Settings;
using Xunit;

namespace MyUniverseUplink.Core.Tests;

public class BootstrapSettingsTests
{
    private const string FullJson = """
        {
          "enabled": true,
          "serverUrl": "https://robertpanai.com",
          "deviceId": "osservatorio-casa",
          "token": "tok_ABC123",
          "extraHeaderName": "x-vercel-protection-bypass",
          "extraHeaderValue": "bypass-value",
          "requireTarget": false,
          "imageTypes": ["light", "FLAT"],
          "thumbnailMode": "always",
          "thumbnailLongEdge": 512,
          "thumbnailQuality": 80,
          "batchSize": 10,
          "htmlReport": true
        }
        """;

    [Fact]
    public void FullDocumentAppliesEveryField()
    {
        var result = BootstrapSettings.Parse(FullJson);

        Assert.NotNull(result);
        var s = result!.Settings;
        Assert.True(s.Enabled);
        Assert.Equal("https://robertpanai.com", s.ServerUrl);
        Assert.Equal("osservatorio-casa", s.DeviceId);
        Assert.Equal("tok_ABC123", s.Token);
        Assert.Equal("x-vercel-protection-bypass", s.ExtraHeaderName);
        Assert.Equal("bypass-value", s.ExtraHeaderValue);
        Assert.False(s.RequireTarget);
        Assert.Equal(new[] { "LIGHT", "FLAT" }, s.ImageTypes);
        Assert.Equal(ThumbnailMode.Always, s.ThumbnailMode);
        Assert.Equal(512, s.ThumbnailLongEdgePx);
        Assert.Equal(80, s.ThumbnailJpegQuality);
        Assert.Equal(10, s.BatchSize);
        Assert.True(s.HtmlReport);
        Assert.Equal(13, result.Applied.Count);
        Assert.Empty(result.Rejected);
        Assert.Empty(result.Ignored);
    }

    [Fact]
    public void PartialDocumentLeavesOtherFieldsUnset()
    {
        var result = BootstrapSettings.Parse("""{ "serverUrl": "http://localhost:3000", "batchSize": 5 }""");

        Assert.NotNull(result);
        Assert.Equal("http://localhost:3000", result!.Settings.ServerUrl);
        Assert.Equal(5, result.Settings.BatchSize);
        Assert.Null(result.Settings.Enabled);
        Assert.Null(result.Settings.Token);
        Assert.Null(result.Settings.ImageTypes);
        Assert.Null(result.Settings.ExtraHeaderName);
        Assert.Equal(2, result.Applied.Count);
        Assert.Contains("serverUrl", result.Applied);
        Assert.Contains("batchSize", result.Applied);
        Assert.Empty(result.Rejected);
    }

    [Fact]
    public void EmptyObjectIsValidAndAppliesNothing()
    {
        var result = BootstrapSettings.Parse("{}");

        Assert.NotNull(result);
        Assert.Empty(result!.Applied);
        Assert.Empty(result.Rejected);
        Assert.Empty(result.Ignored);
    }

    [Fact]
    public void UnknownFieldsAreIgnoredAndNamed()
    {
        var result = BootstrapSettings.Parse(
            """{ "serverUrl": "https://robertpanai.com", "deviceId": "x", "futureField": 42, "nested": { "a": 1 } }""");

        Assert.NotNull(result);
        Assert.Equal("https://robertpanai.com", result!.Settings.ServerUrl);
        Assert.Equal(new[] { "futureField", "nested" }, result.Ignored);
        Assert.Empty(result.Rejected);
    }

    [Fact]
    public void FieldNamesAreCaseInsensitive()
    {
        var result = BootstrapSettings.Parse("""{ "ServerUrl": "https://robertpanai.com" }""");

        Assert.NotNull(result);
        Assert.Equal("https://robertpanai.com", result!.Settings.ServerUrl);
    }

    [Theory]
    [InlineData("""{ "serverUrl": "http://example.com" }""", "serverUrl")]
    [InlineData("""{ "deviceId": "   " }""", "deviceId")]
    [InlineData("""{ "batchSize": 0 }""", "batchSize")]
    [InlineData("""{ "batchSize": "25" }""", "batchSize")]
    [InlineData("""{ "thumbnailLongEdge": 5000 }""", "thumbnailLongEdge")]
    [InlineData("""{ "thumbnailQuality": 10 }""", "thumbnailQuality")]
    [InlineData("""{ "thumbnailMode": "Sometimes" }""", "thumbnailMode")]
    [InlineData("""{ "thumbnailMode": "5" }""", "thumbnailMode")]
    [InlineData("""{ "imageTypes": [] }""", "imageTypes")]
    [InlineData("""{ "imageTypes": "LIGHT" }""", "imageTypes")]
    [InlineData("""{ "imageTypes": ["SNAPSHOT"] }""", "imageTypes")]
    [InlineData("""{ "enabled": "yes" }""", "enabled")]
    [InlineData("""{ "htmlReport": 1 }""", "htmlReport")]
    [InlineData("""{ "token": "has space" }""", "token")]
    [InlineData("""{ "token": "" }""", "token")]
    [InlineData("""{ "extraHeaderName": "x-bad name", "extraHeaderValue": "v" }""", "extraHeaderName")]
    [InlineData("""{ "extraHeaderName": "x-vercel-protection-bypass" }""", "extraHeaderName")]
    [InlineData("""{ "extraHeaderValue": "only-value" }""", "extraHeaderValue")]
    public void InvalidValueIsDiscardedAndNamed(string json, string field)
    {
        var result = BootstrapSettings.Parse(json);

        Assert.NotNull(result);
        Assert.Contains(field, result!.Rejected);
        Assert.DoesNotContain(field, result.Applied);
    }

    [Fact]
    public void InvalidHeaderPairDiscardsBothValues()
    {
        var result = BootstrapSettings.Parse("""{ "extraHeaderName": "x-bad name", "extraHeaderValue": "SECRET-HDR-1" }""");

        Assert.NotNull(result);
        Assert.Null(result!.Settings.ExtraHeaderName);
        Assert.Null(result.Settings.ExtraHeaderValue);
        Assert.Contains("extraHeaderName", result.Rejected);
        Assert.Contains("extraHeaderValue", result.Rejected);
    }

    [Fact]
    public void InvalidFieldDoesNotStopTheOthers()
    {
        var result = BootstrapSettings.Parse("""{ "batchSize": 0, "serverUrl": "https://robertpanai.com" }""");

        Assert.NotNull(result);
        Assert.Null(result!.Settings.BatchSize);
        Assert.Equal("https://robertpanai.com", result.Settings.ServerUrl);
        Assert.Contains("batchSize", result.Rejected);
        Assert.Contains("serverUrl", result.Applied);
    }

    [Fact]
    public void ImageTypesAreNormalizedAndDeduplicated()
    {
        var result = BootstrapSettings.Parse("""{ "imageTypes": [" light ", "LIGHT", "dark"] }""");

        Assert.NotNull(result);
        Assert.Equal(new[] { "LIGHT", "DARK" }, result!.Settings.ImageTypes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1, 2, 3]")]
    [InlineData("\"text\"")]
    public void NonObjectDocumentIsRejectedAsAWhole(string json)
    {
        Assert.Null(BootstrapSettings.Parse(json));
    }

    [Fact]
    public void TokenNeverAppearsInToStringOrNames()
    {
        const string token = "SECRET-TOKEN-XYZ-987";
        const string headerValue = "SECRET-HDR-555";
        var json = $$"""{ "token": "{{token}}", "extraHeaderName": "x-bypass", "extraHeaderValue": "{{headerValue}}" }""";

        var result = BootstrapSettings.Parse(json);

        Assert.NotNull(result);
        Assert.Equal(token, result!.Settings.Token);
        var text = result.Settings.ToString() + result + string.Join(",", result.Applied.Concat(result.Rejected).Concat(result.Ignored));
        Assert.DoesNotContain(token, text);
        Assert.DoesNotContain(headerValue, text);
        Assert.Contains("token=[redacted]", result.Settings.ToString());
    }

    [Fact]
    public void RemoveSecretsDropsTokenAndHeaderValueOnly()
    {
        var clean = BootstrapSettings.RemoveSecrets(FullJson);

        Assert.NotNull(clean);
        Assert.DoesNotContain("tok_ABC123", clean);
        Assert.DoesNotContain("bypass-value", clean);
        Assert.Contains("\"serverUrl\"", clean);
        Assert.Contains("\"batchSize\"", clean);
        Assert.Contains("x-vercel-protection-bypass", clean);
    }

    [Fact]
    public void RemoveSecretsReturnsNullForNonObject()
    {
        Assert.Null(BootstrapSettings.RemoveSecrets("not json"));
    }
}
