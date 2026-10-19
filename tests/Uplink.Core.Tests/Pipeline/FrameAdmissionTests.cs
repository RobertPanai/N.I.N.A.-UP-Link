using MyUniverseUplink.Core.Pipeline;
using Xunit;

namespace MyUniverseUplink.Core.Tests.Pipeline;

public class FrameAdmissionTests
{
    [Fact]
    public void EmptyTypeListDefaultsToLight()
    {
        Assert.Equal(new[] { "LIGHT" }, FrameAdmission.NormalizeImageTypes(null));
        Assert.Equal(new[] { "LIGHT" }, FrameAdmission.NormalizeImageTypes(new[] { "  ", "" }));
    }

    [Fact]
    public void TypesAreNormalizedAndDeduplicated()
    {
        Assert.Equal(new[] { "LIGHT", "FLAT" }, FrameAdmission.NormalizeImageTypes(new[] { "light", " Flat ", "LIGHT" }));
    }

    [Theory]
    [InlineData("LIGHT")]
    [InlineData("light")]
    [InlineData("Light")]
    public void ImageTypeMatchesCaseInsensitively(string type)
    {
        var decision = FrameAdmission.Evaluate(type, hasTarget: true, new[] { "LIGHT" }, requireTarget: true);
        Assert.True(decision.Accepted);
    }

    [Theory]
    [InlineData("DARK")]
    [InlineData("FLAT")]
    [InlineData("")]
    [InlineData(null)]
    public void OtherTypesAreRejectedByDefault(string? type)
    {
        var decision = FrameAdmission.Evaluate(type, hasTarget: true, new[] { "LIGHT" }, requireTarget: true);
        Assert.False(decision.Accepted);
    }

    [Fact]
    public void TargetRequirementIsOptional()
    {
        Assert.False(FrameAdmission.Evaluate("LIGHT", hasTarget: false, new[] { "LIGHT" }, requireTarget: true).Accepted);
        Assert.True(FrameAdmission.Evaluate("LIGHT", hasTarget: false, new[] { "LIGHT" }, requireTarget: false).Accepted);
    }
}
