using System.Text.RegularExpressions;

namespace MyUniverseUplink.Core.Activity;

/// <summary>
/// Pulizia del testo del feed: URL (anche con query), Bearer, coppie token=/secret=/password=, percorsi assoluti Windows
/// e a capo vengono sostituiti; il testo e' limitato a MaxLength caratteri.
/// </summary>
public static class ActivityText
{
    public const int MaxLength = 300;

    private static readonly Regex UrlPattern = new(@"[a-z][a-z0-9+.\-]*://\S+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BearerPattern = new(@"Bearer\s+\S+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SecretPairPattern = new(@"(token|secret|password|apikey|api_key)=\S+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AbsolutePathPattern = new(@"(?:[A-Za-z]:\\|\\\\)\S*", RegexOptions.Compiled);

    public static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var cleaned = UrlPattern.Replace(text, "<url>");
        cleaned = BearerPattern.Replace(cleaned, "Bearer [redacted]");
        cleaned = SecretPairPattern.Replace(cleaned, "$1=[redacted]");
        cleaned = AbsolutePathPattern.Replace(cleaned, "<path>");
        cleaned = cleaned.ReplaceLineEndings(" ").Replace('\t', ' ').Trim();
        return cleaned.Length > MaxLength ? cleaned[..MaxLength] + "..." : cleaned;
    }
}
