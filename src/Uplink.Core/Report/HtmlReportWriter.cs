using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using MyUniverseUplink.Core.Pipeline;

namespace MyUniverseUplink.Core.Report;

/// <param name="Status">Stato corrente tipo "sent" / "pending" / "dead".</param>
public sealed record ReportFrame(
    string FrameId,
    string? TargetName,
    string? Filter,
    DateTimeOffset? CapturedAtUtc,
    string Status,
    string? Detail,
    string? ThumbnailJpegBase64);

public sealed record ReportData(DateTimeOffset GeneratedAtUtc, UplinkStatus Status, IReadOnlyList<ReportFrame> RecentFrames);

/// <summary>Report HTML autonomo (CSS inline, nessuna risorsa esterna). Tutti i valori sono HTML-encoded.</summary>
public sealed partial class HtmlReportWriter
{
    public async Task WriteAsync(string path, ReportData data, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(data);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, Render(data), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken)
                .ConfigureAwait(false);
            File.Move(temp, fullPath, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(temp);
            }
            catch (IOException)
            {
                // Il file temporaneo viene ripulito al prossimo avvio se resta.
            }

            throw;
        }
    }

    public static string Render(ReportData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var s = data.Status;
        var sb = new StringBuilder(8192);

        sb.Append("<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n")
          .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n")
          .Append("<title>My Universe Uplink - report</title>\n<style>\n")
          .Append(":root{color-scheme:dark;--bg:#05070A;--panel:#0d1322;--text:#e6ecff;--muted:#8ea0c8;--accent:#7da0ff;--line:rgba(148,168,255,.24)}\n")
          .Append("body{margin:0;padding:16px;background:var(--bg);color:var(--text);font:14px/1.5 system-ui,sans-serif}\n")
          .Append("h1{font-size:20px;margin:0 0 4px;color:var(--accent)}h2{font-size:16px;margin:24px 0 8px}\n")
          .Append(".muted{color:var(--muted)}.panel{background:var(--panel);border:1px solid var(--line);border-radius:10px;padding:12px;overflow-x:auto}\n")
          .Append("table{width:100%;border-collapse:collapse}td,th{padding:6px 8px;border-bottom:1px solid var(--line);text-align:left;vertical-align:top}\n")
          .Append("th{color:var(--muted);font-weight:600}img{width:96px;height:auto;border-radius:6px}\n")
          .Append("code{font-family:ui-monospace,monospace;font-size:12px;word-break:break-all}\n")
          .Append("</style>\n</head>\n<body>\n");

        sb.Append("<h1>My Universe Uplink</h1>\n")
          .Append("<p class=\"muted\">Generated ").Append(E(FormatUtc(data.GeneratedAtUtc))).Append("</p>\n");

        sb.Append("<div class=\"panel\"><table>\n");
        Row(sb, "State", s.State.ToString());
        Row(sb, "Pending", s.Pending.ToString(CultureInfo.InvariantCulture));
        Row(sb, "Dead", s.Dead.ToString(CultureInfo.InvariantCulture));
        Row(sb, "Sent (total)", s.Sent.ToString(CultureInfo.InvariantCulture));
        Row(sb, "Sent tonight", s.SentTonight.ToString(CultureInfo.InvariantCulture));
        Row(sb, "Last success", s.LastSuccessAtUtc is { } ok ? FormatUtc(ok) : "-");
        Row(sb, "Last error", s.LastError is null ? "-" : $"{s.LastError} ({(s.LastErrorAtUtc is { } at ? FormatUtc(at) : "?")})");
        sb.Append("</table></div>\n");

        sb.Append("<h2>Recent frames</h2>\n<div class=\"panel\"><table>\n")
          .Append("<tr><th>Captured (UTC)</th><th>Target</th><th>Filter</th><th>Status</th><th>Detail</th><th>Thumbnail</th></tr>\n");

        foreach (var frame in data.RecentFrames)
        {
            sb.Append("<tr><td>").Append(E(frame.CapturedAtUtc is { } c ? FormatUtc(c) : "-"))
              .Append("</td><td>").Append(E(frame.TargetName))
              .Append("</td><td>").Append(E(frame.Filter))
              .Append("</td><td>").Append(E(frame.Status))
              .Append("</td><td><code>").Append(E(frame.Detail))
              .Append("</code><br><span class=\"muted\"><code>").Append(E(frame.FrameId)).Append("</code></span></td><td>");

            if (frame.ThumbnailJpegBase64 is { Length: > 0 } thumb && Base64Regex().IsMatch(thumb))
            {
                sb.Append("<img alt=\"\" src=\"data:image/jpeg;base64,").Append(E(thumb)).Append("\">");
            }

            sb.Append("</td></tr>\n");
        }

        sb.Append("</table></div>\n</body>\n</html>\n");
        return sb.ToString();
    }

    private static void Row(StringBuilder sb, string label, string value)
        => sb.Append("<tr><th>").Append(E(label)).Append("</th><td>").Append(E(value)).Append("</td></tr>\n");

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private static string FormatUtc(DateTimeOffset value)
        => value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);

    [GeneratedRegex("^[A-Za-z0-9+/=]+$")]
    private static partial Regex Base64Regex();
}
