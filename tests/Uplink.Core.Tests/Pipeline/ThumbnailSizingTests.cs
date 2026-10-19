using MyUniverseUplink.Core.Pipeline;
using Xunit;

namespace MyUniverseUplink.Core.Tests.Pipeline;

public class ThumbnailSizingTests
{
    [Fact]
    public void LongEdgeIsFittedAndAspectKept()
    {
        Assert.Equal((400, 267), ThumbnailSizing.Fit(6248, 4176, 400));
        Assert.Equal((267, 400), ThumbnailSizing.Fit(4176, 6248, 400));
    }

    [Fact]
    public void SmallImagesAreNeverUpscaled()
    {
        Assert.Equal((300, 200), ThumbnailSizing.Fit(300, 200, 400));
    }

    [Fact]
    public void ExtremeAspectKeepsAtLeastOnePixel()
    {
        Assert.Equal((1, 100), ThumbnailSizing.Fit(1, 1000, 100));
    }

    [Fact]
    public void InvalidSizesThrow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ThumbnailSizing.Fit(0, 10, 400));
        Assert.Throws<ArgumentOutOfRangeException>(() => ThumbnailSizing.Fit(10, 10, 0));
    }
}
