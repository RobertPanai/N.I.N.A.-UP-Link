using MyUniverseUplink.Core.Pipeline;
using MyUniverseUplink.Core.Report;
using MyUniverseUplink.Core.Tests.Support;
using Xunit;

namespace MyUniverseUplink.Core.Tests.Report;

public class HtmlReportWriterTests
{
    private static readonly UplinkStatus Status = new(UplinkState.Retrying, 3, 1, 10, 4, "HTTP 503", DateTimeOffset.UtcNow, null);

    private static ReportData Data(params ReportFrame[] frames) =>
        new(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), Status, frames);

    [Fact]
    public void EveryDynamicValueIsHtmlEncoded()
    {
        var frame = new ReportFrame(
            "abc123",
            "<script>alert('x')</script>",
            "\"Ha\" & <b>",
            new DateTimeOffset(2026, 1, 2, 1, 0, 0, TimeSpan.Zero),
            "dead",
            "rejected by server: <img src=x onerror=alert(1)>",
            null);

        var html = HtmlReportWriter.Render(Data(frame));

        Assert.DoesNotContain("<script>alert", html);
        Assert.Contains("&lt;script&gt;alert(&#39;x&#39;)&lt;/script&gt;", html);
        Assert.Contains("&lt;img src=x onerror=alert(1)&gt;", html);
        Assert.DoesNotContain("<img src=x", html);
    }

    [Fact]
    public void ThumbnailIsEmbeddedOnlyWhenItIsBase64()
    {
        var good = new ReportFrame("g", "T", "Ha", null, "sent", null, "/9j/4AAQSkZJRg==");
        var bad = new ReportFrame("b", "T", "Ha", null, "sent", null, "x\"onerror=alert(1)");

        var goodHtml = HtmlReportWriter.Render(Data(good));
        var badHtml = HtmlReportWriter.Render(Data(bad));

        Assert.Contains("data:image/jpeg;base64,/9j/4AAQSkZJRg==", goodHtml);
        Assert.DoesNotContain("data:image/jpeg", badHtml);
        Assert.DoesNotContain("onerror=alert(1)\"", badHtml);
    }

    [Fact]
    public void StatusSectionShowsCountersAndLastError()
    {
        var html = HtmlReportWriter.Render(Data());

        Assert.Contains("Retrying", html);
        Assert.Contains("HTTP 503", html);
        Assert.Contains("Sent tonight", html);
    }

    [Fact]
    public async Task WriteAsyncReplacesTheFileAndLeavesNoTempFiles()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "report", "uplink.html");
        var writer = new HtmlReportWriter();

        await writer.WriteAsync(path, Data(), CancellationToken.None);
        await writer.WriteAsync(path, Data(new ReportFrame("n", "Second", "Ha", null, "pending", null, null)), CancellationToken.None);

        var text = File.ReadAllText(path);
        Assert.Contains("Second", text);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
    }
}
