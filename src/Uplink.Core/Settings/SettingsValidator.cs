using System.Text.RegularExpressions;

namespace MyUniverseUplink.Core.Settings;

/// <summary>Validazione delle impostazioni. Restituisce i messaggi di errore; lista vuota = valido.</summary>
public static partial class SettingsValidator
{
    public static IReadOnlyList<string> Validate(UplinkSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(settings.ServerUrl))
        {
            errors.Add("Server URL is required.");
        }
        else if (!Uri.TryCreate(settings.ServerUrl.Trim(), UriKind.Absolute, out var uri) || !IsAllowedServerUri(uri))
        {
            errors.Add("Server URL must use https (http is allowed only for localhost or 127.0.0.1).");
        }

        if (string.IsNullOrWhiteSpace(settings.DeviceId))
        {
            errors.Add("Device ID is required.");
        }

        CheckRange(errors, settings.BatchSize, 1, 100, "Batch size");
        CheckRange(errors, settings.BatchIntervalSeconds, 1, 3600, "Batch interval (seconds)");
        CheckRange(errors, settings.TimeoutSeconds, 10, 120, "HTTP timeout (seconds)");
        CheckRange(errors, settings.ThumbnailLongEdgePx, 100, 1600, "Thumbnail long edge (px)");
        CheckRange(errors, settings.ThumbnailJpegQuality, 30, 95, "Thumbnail JPEG quality");

        var hasName = !string.IsNullOrWhiteSpace(settings.ExtraHeaderName);
        var hasValue = !string.IsNullOrEmpty(settings.ExtraHeaderValue);
        if (hasName && !HeaderNameRegex().IsMatch(settings.ExtraHeaderName!.Trim()))
        {
            errors.Add("Extra header name contains invalid characters.");
        }

        if (hasName != hasValue)
        {
            errors.Add("Extra header needs both a name and a value.");
        }

        return errors;
    }

    /// <summary>https sempre; http solo verso loopback (NFR-05).</summary>
    public static bool IsAllowedServerUri(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri)
        {
            return false;
        }

        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            return true;
        }

        return uri.Scheme == Uri.UriSchemeHttp
            && (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || uri.Host == "127.0.0.1");
    }

    private static void CheckRange(List<string> errors, int value, int min, int max, string label)
    {
        if (value < min || value > max)
        {
            errors.Add($"{label} must be between {min} and {max}.");
        }
    }

    [GeneratedRegex("^[A-Za-z0-9-]+$")]
    private static partial Regex HeaderNameRegex();
}
