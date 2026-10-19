using MyUniverseUplink.Core.Pipeline;
using Xunit;

namespace MyUniverseUplink.Core.Tests.Pipeline;

public class StretchMathTests
{
    [Fact]
    public void PercentilesOfUniformHistogram()
    {
        var histogram = new long[100];
        Array.Fill(histogram, 1L);

        var (low, high) = StretchMath.PercentileBins(histogram, 0.5, 99.5);

        Assert.Equal(0, low);
        Assert.Equal(99, high);
    }

    [Fact]
    public void PercentilesIgnoreOutliersAtTheEnds()
    {
        var histogram = new long[256];
        histogram[10] = 1000;
        histogram[200] = 1000;
        histogram[255] = 1;

        var (low, high) = StretchMath.PercentileBins(histogram, 0.5, 99.5);

        Assert.Equal(10, low);
        Assert.Equal(200, high);
    }

    [Fact]
    public void EmptyHistogramReturnsFullRange()
    {
        var histogram = new long[16];
        Assert.Equal((0, 15), StretchMath.PercentileBins(histogram, 0.5, 99.5));
    }

    [Fact]
    public void MapClampsAndAppliesGamma()
    {
        Assert.Equal(0.0, StretchMath.Map(5, 10, 20, 2.2));
        Assert.Equal(1.0, StretchMath.Map(25, 10, 20, 2.2));
        Assert.Equal(0.5, StretchMath.Map(15, 10, 20, 1.0), precision: 6);
        Assert.True(StretchMath.Map(15, 10, 20, 2.2) > 0.5);
    }

    [Fact]
    public void DegenerateRangeDoesNotDivideByZero()
    {
        Assert.Equal(1.0, StretchMath.Map(7, 7, 7, 2.2));
        Assert.Equal(0.0, StretchMath.Map(6, 7, 7, 2.2));
    }
}
