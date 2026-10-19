using System.Security.Cryptography;
using System.Text;
using MyUniverseUplink.Core.Identity;
using Xunit;

namespace MyUniverseUplink.Core.Tests;

public class FrameIdFactoryTests
{
    private static readonly DateTimeOffset Captured = new(2025, 11, 7, 9, 16, 58, 892, TimeSpan.Zero);

    [Fact]
    public void MatchesIndependentlyComputedSha256Prefix()
    {
        var expected = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes("prof|QHY268M|2025-11-07T09:16:58.892Z|WitchHead_Ha")))[..32]
            .ToLowerInvariant();

        var id = FrameIdFactory.Create("prof", "QHY268M", Captured, "WitchHead_Ha.fits");

        Assert.Equal(expected, id);
        Assert.Equal(32, id.Length);
        Assert.Matches("^[0-9a-f]{32}$", id);
    }

    [Fact]
    public void IsDeterministic()
    {
        var a = FrameIdFactory.Create("p", "c", Captured, "x.fits");
        var b = FrameIdFactory.Create("p", "c", Captured, "x.fits");
        Assert.Equal(a, b);
    }

    [Fact]
    public void IgnoresExtensionSoFitsAndXisfShareTheId()
    {
        var fits = FrameIdFactory.Create("p", "c", Captured, "WitchHead_Ha_900s_0012.fits");
        var xisf = FrameIdFactory.Create("p", "c", Captured, "WitchHead_Ha_900s_0012.xisf");
        Assert.Equal(fits, xisf);
    }

    [Fact]
    public void IgnoresDirectoryPartOfFileName()
    {
        var bare = FrameIdFactory.Create("p", "c", Captured, "frame.fits");
        var windows = FrameIdFactory.Create("p", "c", Captured, @"D:\Img\2025\frame.fits");
        var unix = FrameIdFactory.Create("p", "c", Captured, "/data/frame.fits");
        Assert.Equal(bare, windows);
        Assert.Equal(bare, unix);
    }

    [Fact]
    public void KeepsDotsInsideTheStemExceptTheLastExtension()
    {
        Assert.Equal("a.b", FrameIdFactory.StripExtension("a.b.fits"));
        Assert.Equal(".hidden", FrameIdFactory.StripExtension(".hidden"));
    }

    [Fact]
    public void IsSensitiveToEachComponent()
    {
        var baseline = FrameIdFactory.Create("p", "c", Captured, "f.fits");

        Assert.NotEqual(baseline, FrameIdFactory.Create("q", "c", Captured, "f.fits"));
        Assert.NotEqual(baseline, FrameIdFactory.Create("p", "d", Captured, "f.fits"));
        Assert.NotEqual(baseline, FrameIdFactory.Create("p", "c", Captured.AddMilliseconds(1), "f.fits"));
        Assert.NotEqual(baseline, FrameIdFactory.Create("p", "c", Captured, "g.fits"));
    }

    [Fact]
    public void UsesUtcSoOffsetDoesNotChangeTheId()
    {
        var local = Captured.ToOffset(TimeSpan.FromHours(1));
        Assert.Equal(
            FrameIdFactory.Create("p", "c", Captured, "f.fits"),
            FrameIdFactory.Create("p", "c", local, "f.fits"));
    }
}
