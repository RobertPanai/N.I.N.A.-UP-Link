using MyUniverseUplink.Core.Settings;
using Xunit;

namespace MyUniverseUplink.Core.Tests;

public class SettingsValidatorTests
{
    private static UplinkSettings Valid() => new()
    {
        ServerUrl = "https://robertpanai.com",
        DeviceId = "osservatorio-casa",
    };

    [Fact]
    public void DefaultsWithServerAndDeviceAreValid()
    {
        Assert.Empty(SettingsValidator.Validate(Valid()));
    }

    [Fact]
    public void EmptyServerAndDeviceAreRejected()
    {
        var errors = SettingsValidator.Validate(new UplinkSettings());
        Assert.Contains(errors, e => e.Contains("Server URL"));
        Assert.Contains(errors, e => e.Contains("Device ID"));
    }

    [Theory]
    [InlineData("http://robertpanai.com")]
    [InlineData("ftp://robertpanai.com")]
    [InlineData("robertpanai.com")]
    public void NonHttpsRemoteUrlsAreRejected(string url)
    {
        var settings = Valid();
        settings.ServerUrl = url;
        Assert.NotEmpty(SettingsValidator.Validate(settings));
    }

    [Theory]
    [InlineData("http://localhost:3000")]
    [InlineData("http://127.0.0.1:3000")]
    [InlineData("https://localhost")]
    public void LoopbackHttpIsAllowed(string url)
    {
        var settings = Valid();
        settings.ServerUrl = url;
        Assert.Empty(SettingsValidator.Validate(settings));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void BatchSizeRange(int value, bool valid)
    {
        var settings = Valid();
        settings.BatchSize = value;
        Assert.Equal(valid, SettingsValidator.Validate(settings).Count == 0);
    }

    [Theory]
    [InlineData(99, false)]
    [InlineData(100, true)]
    [InlineData(1600, true)]
    [InlineData(1601, false)]
    public void ThumbnailLongEdgeRange(int value, bool valid)
    {
        var settings = Valid();
        settings.ThumbnailLongEdgePx = value;
        Assert.Equal(valid, SettingsValidator.Validate(settings).Count == 0);
    }

    [Theory]
    [InlineData(29, false)]
    [InlineData(30, true)]
    [InlineData(95, true)]
    [InlineData(96, false)]
    public void ThumbnailQualityRange(int value, bool valid)
    {
        var settings = Valid();
        settings.ThumbnailJpegQuality = value;
        Assert.Equal(valid, SettingsValidator.Validate(settings).Count == 0);
    }

    [Fact]
    public void ExtraHeaderNeedsNameAndValueTogether()
    {
        var settings = Valid();
        settings.ExtraHeaderName = "x-vercel-protection-bypass";
        Assert.Contains(SettingsValidator.Validate(settings), e => e.Contains("both a name and a value"));

        settings.ExtraHeaderValue = "secret";
        Assert.Empty(SettingsValidator.Validate(settings));
    }

    [Fact]
    public void ExtraHeaderNameMustBeAToken()
    {
        var settings = Valid();
        settings.ExtraHeaderName = "bad name:";
        settings.ExtraHeaderValue = "v";
        Assert.Contains(SettingsValidator.Validate(settings), e => e.Contains("invalid characters"));
    }

    [Fact]
    public void ErrorsNeverEchoTheExtraHeaderValue()
    {
        var settings = Valid();
        settings.ExtraHeaderValue = "super-secret-value";
        settings.BatchSize = 0;
        Assert.DoesNotContain(SettingsValidator.Validate(settings), e => e.Contains("super-secret-value"));
    }
}
